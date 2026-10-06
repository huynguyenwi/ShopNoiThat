using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FurnitureStore.Application.Chat;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

/// <summary>Phase 7 end to end: REST chat API, authorization, and realtime delivery through the SignalR hub.</summary>
public sealed partial class ChatWebTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>A browser-like visitor: one cookie jar shared by its HttpClient and its SignalR connection.</summary>
    private sealed class Visitor(FurnitureStoreWebApplicationFactory factory) : IAsyncDisposable
    {
        private readonly CookieContainer _cookies = new();
        private readonly List<HubConnection> _connections = [];

        public static Visitor Create(FurnitureStoreWebApplicationFactory factory) => new(factory);

        public HttpClient Client => _client ??= new HttpClient(new CookieContainerHandler(_cookies) { InnerHandler = factory.Server.CreateHandler() })
        {
            BaseAddress = new Uri("https://localhost")
        };
        private HttpClient? _client;

        public async Task<string> CsrfAsync()
        {
            var html = await Client.GetStringAsync("/contact");
            return WebUtility.HtmlDecode(CsrfMetaRegex().Match(html).Groups[1].Value);
        }

        public async Task<HttpResponseMessage> PostJsonAsync(string url, object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync());
            return await Client.SendAsync(request);
        }

        public async Task LoginAsync(string email, string password)
        {
            var response = await WebTestHelpers.LoginAsync(Client, email, password);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        public async Task<HubConnection> ConnectAsync(string? origin = null)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl("https://localhost/hubs/chat", options =>
                {
                    options.Transports = HttpTransportType.LongPolling; // TestServer has no real sockets
                    options.HttpMessageHandlerFactory = _ => new CookieContainerHandler(_cookies) { InnerHandler = factory.Server.CreateHandler() };
                    if (origin is not null) options.Headers["Origin"] = origin;
                })
                .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
                .Build();
            _connections.Add(connection);
            await connection.StartAsync();
            return connection;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var connection in _connections) await connection.DisposeAsync();
            _client?.Dispose();
        }
    }

    private sealed record Envelope<T>(bool Success, string Message, T? Data, List<string> Errors);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<Envelope<T>> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Envelope<T>>(Json))!;

    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private async Task<Visitor> StartedGuestAsync(string name)
    {
        var guest = Visitor.Create(factory);
        var start = await guest.PostJsonAsync("/api/chat/start", new { name, phone = "0912345678" });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        return guest;
    }

    private async Task<Visitor> AdminAsync()
    {
        var admin = Visitor.Create(factory);
        await admin.LoginAsync(FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        return admin;
    }

    // ------------------------------------------------------------------ REST API

    [Fact]
    public async Task Guest_StartsChat_GetsHttpOnlyCookie_AndCanSendThroughTheApi()
    {
        await using var guest = Visitor.Create(factory);

        var initial = await ReadAsync<JsonElement>(await guest.Client.GetAsync("/api/chat"));
        Assert.True(initial.Data.GetProperty("requiresContactInfo").GetBoolean());

        var invalid = await guest.PostJsonAsync("/api/chat/start", new { name = "", phone = "123" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains((await ReadAsync<object>(invalid)).Errors, e => e.Contains("tên"));

        var start = await guest.PostJsonAsync("/api/chat/start", new { name = "Chị Lan", phone = "0912345678" });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var cookie = Assert.Single(start.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".NhaMoc.Chat="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);

        var sent = await guest.PostJsonAsync("/api/chat/messages", new { content = "Bàn ăn 6 ghế giá bao nhiêu?" });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        var chat = await ReadAsync<JsonElement>(await guest.Client.GetAsync("/api/chat"));
        Assert.Equal("Bàn ăn 6 ghế giá bao nhiêu?", chat.Data.GetProperty("messages")[0].GetProperty("content").GetString());
        var unread = (await ReadAsync<JsonElement>(await guest.Client.GetAsync("/api/chat/unread"))).Data;
        Assert.True(unread.GetProperty("hasConversation").GetBoolean());
        Assert.Equal(0, unread.GetProperty("count").GetInt32()); // no reply from the shop yet
    }

    [Fact]
    public async Task ChatApi_WithoutCsrfHeader_IsRejected()
    {
        await using var guest = Visitor.Create(factory);
        await guest.Client.GetAsync("/contact");

        var response = await guest.Client.PostAsync("/api/chat/start", JsonContent.Create(new { name = "CSRF", phone = "0912345678" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminChatApi_RequiresBackOfficeRole()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/chat/conversations")).StatusCode);

        var customer = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword);
        var forbidden = await customer.GetAsync("/api/admin/chat/conversations");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.False((await ReadAsync<object>(forbidden)).Success);

        var page = await customer.GetAsync("/admin/chat");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/account/access-denied", page.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Staff_CanAnswerChats_ButCannotOpenOtherAdminPages()
    {
        var email = $"staff-{Guid.NewGuid():N}@furniture.local";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, FullName = "Nhân viên Test", EmailConfirmed = true, IsActive = true, CreatedAt = DateTime.UtcNow };
            Assert.True((await users.CreateAsync(user, "Staff@Test123")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, AppRoles.Staff)).Succeeded);
        }

        await using var staff = Visitor.Create(factory);
        await staff.LoginAsync(email, "Staff@Test123");

        var chatPage = await staff.Client.GetAsync("/admin/chat");
        Assert.Equal(HttpStatusCode.OK, chatPage.StatusCode);
        var html = await chatPage.Content.ReadAsStringAsync();
        Assert.Contains("id=\"adminChat\"", html);
        Assert.DoesNotContain("href=\"/admin/orders\"", html); // admin-only modules are not listed for staff

        Assert.Equal(HttpStatusCode.OK, (await staff.Client.GetAsync("/api/admin/chat/conversations")).StatusCode);
        var dashboard = await staff.Client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, dashboard.StatusCode);
        Assert.Contains("/account/access-denied", dashboard.Headers.Location!.ToString());
    }

    [Fact]
    public async Task AdminChatPage_AndWidget_RenderForTheRightPeople()
    {
        await using var admin = await AdminAsync();
        var adminPage = await admin.Client.GetStringAsync("/admin/chat");
        Assert.Contains("signalr.min.js", adminPage);
        Assert.Contains("id=\"adminChatBadge\"", adminPage);
        Assert.DoesNotContain("id=\"chatWidget\"", await admin.Client.GetStringAsync("/")); // staff answer from /admin/chat

        var home = await factory.CreateClient().GetStringAsync("/");
        Assert.Contains("id=\"chatWidget\"", home);
        Assert.Contains("chat-widget.js", home);
    }

    [Fact]
    public async Task GuestChat_FollowsTheVisitor_WhenTheyRegister()
    {
        await using var guest = await StartedGuestAsync("Khách chưa có tài khoản");
        Assert.Equal(HttpStatusCode.OK, (await guest.PostJsonAsync("/api/chat/messages", new { content = "Hỏi trước khi đăng ký" })).StatusCode);

        var register = await RegisterAsync(guest.Client);

        var chat = (await ReadAsync<JsonElement>(await guest.Client.GetAsync("/api/chat"))).Data;
        Assert.True(chat.GetProperty("signedIn").GetBoolean());
        Assert.Equal("Khách Thử Nghiệm", chat.GetProperty("conversation").GetProperty("customerName").GetString());
        Assert.False(chat.GetProperty("conversation").GetProperty("isGuest").GetBoolean());
        Assert.Equal("Hỏi trước khi đăng ký", chat.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.NotNull(register);
    }

    // ------------------------------------------------------------------ SignalR

    [Fact]
    public async Task Realtime_GuestAndAdmin_ExchangeMessagesThroughTheHub()
    {
        await using var guest = await StartedGuestAsync("Anh Tuấn");
        await using var admin = await AdminAsync();

        var adminReceived = new TaskCompletionSource<(ChatMessageDto Message, ChatConversationDto Conversation)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var guestReceived = new TaskCompletionSource<ChatMessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adminConnection = await admin.ConnectAsync();
        adminConnection.On<ChatMessageDto, ChatConversationDto>("ReceiveMessage", (m, c) =>
        {
            if (m.SenderType == ChatSenderType.Customer && c.CustomerName == "Anh Tuấn") adminReceived.TrySetResult((m, c));
        });
        var guestConnection = await guest.ConnectAsync();
        guestConnection.On<ChatMessageDto, ChatConversationDto>("ReceiveMessage", (m, _) =>
        {
            if (m.SenderType == ChatSenderType.Staff) guestReceived.TrySetResult(m);
        });

        // Guest → shop
        var sent = await guestConnection.InvokeAsync<ChatMessageDto>("SendMessage", "Giường 1m8 có sẵn không?", (int?)null);
        var (atAdmin, conversation) = await adminReceived.Task.WaitAsync(Timeout);
        Assert.Equal(sent.Id, atAdmin.Id);
        Assert.Equal("Giường 1m8 có sẵn không?", atAdmin.Content);
        Assert.True(conversation.IsGuest);

        // Shop → guest
        var reply = await adminConnection.InvokeAsync<ChatMessageDto>("SendStaffMessage", conversation.Id, "Dạ có sẵn ạ");
        var atGuest = await guestReceived.Task.WaitAsync(Timeout);
        Assert.Equal(reply.Id, atGuest.Id);
        Assert.Equal("Dạ có sẵn ạ", atGuest.Content);

        // Both messages are persisted.
        Assert.Equal(2, await DbAsync(db => db.ChatMessages.CountAsync(m => m.ConversationId == conversation.Id)));
    }

    [Fact]
    public async Task Realtime_OtherCustomers_DoNotReceiveSomeoneElsesMessages()
    {
        await using var alice = await StartedGuestAsync("Alice");
        await using var bob = await StartedGuestAsync("Bob");
        await using var admin = await AdminAsync();

        var bobGotSomething = false;
        var bobConnection = await bob.ConnectAsync();
        bobConnection.On<ChatMessageDto, ChatConversationDto>("ReceiveMessage", (_, _) => bobGotSomething = true);
        var aliceConnection = await alice.ConnectAsync();
        var adminConnection = await admin.ConnectAsync();
        var adminGot = new TaskCompletionSource<ChatConversationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        adminConnection.On<ChatMessageDto, ChatConversationDto>("ReceiveMessage", (m, c) => { if (c.CustomerName == "Alice") adminGot.TrySetResult(c); });

        await aliceConnection.InvokeAsync<ChatMessageDto>("SendMessage", "Riêng tư", (int?)null);
        var conversation = await adminGot.Task.WaitAsync(Timeout);
        await adminConnection.InvokeAsync<ChatMessageDto>("SendStaffMessage", conversation.Id, "Trả lời Alice");
        await Task.Delay(500);

        Assert.False(bobGotSomething);
    }

    [Fact]
    public async Task Realtime_CustomerCannotCallStaffMethods_AndErrorsAreFriendly()
    {
        await using var guest = await StartedGuestAsync("Khách tò mò");
        var connection = await guest.ConnectAsync();
        var conversationId = await DbAsync(db => db.ChatConversations.Where(c => c.CustomerName == "Khách tò mò").Select(c => c.Id).SingleAsync());

        var forbidden = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync<ChatMessageDto>("SendStaffMessage", conversationId, "Mạo danh nhân viên"));
        Assert.Contains("unauthorized", forbidden.Message, StringComparison.OrdinalIgnoreCase);

        var empty = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync<ChatMessageDto>("SendMessage", "   ", (int?)null));
        Assert.Contains("Vui lòng nhập nội dung tin nhắn.", empty.Message);

        Assert.Equal(0, await DbAsync(db => db.ChatMessages.CountAsync(m => m.ConversationId == conversationId)));
    }

    [Fact]
    public async Task Realtime_CrossOriginConnections_AreRejected()
    {
        await using var guest = await StartedGuestAsync("Nạn nhân");

        // Control: the site's own origin works.
        var sameOrigin = await guest.ConnectAsync(origin: "https://localhost");
        Assert.Equal("Cùng origin", (await sameOrigin.InvokeAsync<ChatMessageDto>("SendMessage", "Cùng origin", (int?)null)).Content);

        HubConnection? connection = null;
        try
        {
            connection = await guest.ConnectAsync(origin: "https://evil.example.com");
        }
        catch (Exception)
        {
            return; // refused during start: fine
        }

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        if (connection.State != HubConnectionState.Disconnected)
        {
            await closed.Task.WaitAsync(Timeout);
        }

        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMetaRegex();
}
