using FurnitureStore.Application.Chat;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Admin;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Chat;

/// <summary>Customer ↔ shop chat rules (Phase 7): identities, validation, unread counters, notifications, rate limit.</summary>
public sealed class ChatServiceTests : IAsyncLifetime
{
    private ServiceTestHost _host = null!;
    private ChatStaff Staff = null!;

    public async Task InitializeAsync()
    {
        _host = await ServiceTestHost.CreateAsync();
        Staff = new ChatStaff(await AdminServiceTests.CreateCustomerAsync(_host, "mai@furniture.local"), "Nhân viên Mai"); // FK needs a real user
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Chat<T>(Func<IChatService, Task<T>> action) => _host.RunAsync(sp => action(sp.GetRequiredService<IChatService>()));
    private Task Chat(Func<IChatService, Task> action) => _host.RunAsync(sp => action(sp.GetRequiredService<IChatService>()));
    private Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action) => _host.RunAsync(sp => action(sp.GetRequiredService<ApplicationDbContext>()));

    private static ChatCustomer NewGuest() => new(null, Guid.NewGuid().ToString("N"));

    private async Task<ChatCustomer> StartedGuestAsync(string name = "Chị Hoa")
    {
        var guest = NewGuest();
        await Chat(c => c.StartAsync(guest, new StartChatCommand { Name = name, Phone = "0912345678" }));
        return guest;
    }

    private Task<ChatMessageDto> SayAsync(ChatCustomer customer, string text, int? productId = null) =>
        Chat(c => c.SendFromCustomerAsync(customer, new SendChatMessageCommand { Content = text, ProductId = productId }));

    // ------------------------------------------------------------------ guests

    [Fact]
    public async Task Guest_MustLeaveContactDetails_BeforeChatting()
    {
        var guest = NewGuest();

        Assert.True((await Chat(c => c.GetForCustomerAsync(guest))).RequiresContactInfo);
        await Assert.ThrowsAsync<BusinessRuleException>(() => SayAsync(guest, "Xin chào"));

        var missing = await Assert.ThrowsAsync<AppValidationException>(() => Chat(c => c.StartAsync(guest, new StartChatCommand { Name = "" })));
        Assert.True(missing.FieldErrors.ContainsKey(nameof(StartChatCommand.Name)));
        Assert.True(missing.FieldErrors.ContainsKey(nameof(StartChatCommand.Phone))); // phone or email required

        var badPhone = await Assert.ThrowsAsync<AppValidationException>(() => Chat(c => c.StartAsync(guest, new StartChatCommand { Name = "An", Phone = "12345" })));
        Assert.True(badPhone.FieldErrors.ContainsKey(nameof(StartChatCommand.Phone)));

        var started = await Chat(c => c.StartAsync(guest, new StartChatCommand { Name = "  Anh An ", Email = "an@example.com" }));
        Assert.Equal("Anh An", started.CustomerName);
        Assert.True(started.IsGuest);
        Assert.False((await Chat(c => c.GetForCustomerAsync(guest))).RequiresContactInfo);
    }

    [Fact]
    public async Task CustomerMessage_IsSaved_CountsAsUnread_NotifiesAdminsOncePerWave_AndIsPushed()
    {
        var guest = await StartedGuestAsync();

        var first = await SayAsync(guest, "Sofa Oslo còn màu xanh không ạ?");
        await SayAsync(guest, "Mình ở Hà Nội");

        var conversation = await Db(db => db.ChatConversations.AsNoTracking().SingleAsync());
        Assert.Equal(2, conversation.StaffUnreadCount);
        Assert.Equal("Mình ở Hà Nội", conversation.LastMessagePreview);
        Assert.Equal(ChatSenderType.Customer, first.SenderType);
        Assert.Equal("Chị Hoa", first.SenderName);
        Assert.Equal(1, await Db(db => db.Notifications.CountAsync(n => n.RecipientRole == AppRoles.Admin && n.Type == NotificationType.NewChatMessage)));

        Assert.Equal(2, _host.ChatEvents.Sent.Count);
        Assert.All(_host.ChatEvents.Sent, e => Assert.Equal(guest.GuestKey, e.Customer.GuestKey));

        // After the shop answers, the next customer message starts a new "wave" and notifies again.
        await Chat(c => c.SendFromStaffAsync(Staff, conversation.Id, "Dạ còn ạ"));
        await SayAsync(guest, "Cảm ơn shop");
        Assert.Equal(2, await Db(db => db.Notifications.CountAsync(n => n.Type == NotificationType.NewChatMessage && n.RecipientRole == AppRoles.Admin)));
    }

    [Fact]
    public async Task SignedInCustomer_ConversationIsCreatedOnFirstMessage_WithProductContext()
    {
        var userId = await AdminServiceTests.CreateCustomerAsync(_host);
        var customer = new ChatCustomer(userId, null, "Anh Minh", "minh@example.com");
        var product = await Db(db => db.Products.Where(p => p.Status == ProductStatus.Active).Select(p => new { p.Id, p.Name }).FirstAsync());

        var before = await Chat(c => c.GetForCustomerAsync(customer));
        Assert.Null(before.Conversation);
        Assert.False(before.RequiresContactInfo);

        await SayAsync(customer, "Tư vấn giúp mình mẫu này", product.Id);

        var chat = await Chat(c => c.GetForCustomerAsync(customer));
        Assert.Equal("Anh Minh", chat.Conversation!.CustomerName);
        Assert.Equal("minh@example.com", chat.Conversation.CustomerEmail);
        Assert.Equal(product.Name, chat.Conversation.ProductName);
        Assert.False(chat.Conversation.IsGuest);
        Assert.Single(chat.Messages);
    }

    [Fact]
    public async Task Customers_OnlySeeTheirOwnConversation()
    {
        var alice = await StartedGuestAsync("Alice");
        var bob = await StartedGuestAsync("Bob");
        await SayAsync(alice, "Tin nhắn riêng của Alice");

        var bobView = await Chat(c => c.GetForCustomerAsync(bob));

        Assert.Empty(bobView.Messages);
        Assert.Equal("Bob", bobView.Conversation!.CustomerName);
        Assert.Empty(await Chat(c => c.GetOlderForCustomerAsync(bob, int.MaxValue)));
    }

    // ------------------------------------------------------------------ staff

    [Fact]
    public async Task StaffReply_UpdatesCounters_MarksCustomerMessagesRead_AndNotifiesRegisteredCustomer()
    {
        var userId = await AdminServiceTests.CreateCustomerAsync(_host);
        var customer = new ChatCustomer(userId, null, "Anh Minh");
        await SayAsync(customer, "Giường ngủ giao mấy ngày?");
        var conversationId = await Db(db => db.ChatConversations.Select(c => c.Id).SingleAsync());

        var reply = await Chat(c => c.SendFromStaffAsync(Staff, conversationId, "Dạ 3-5 ngày anh nhé"));
        await Chat(c => c.SendFromStaffAsync(Staff, conversationId, "Nội thành Hà Nội thì 2 ngày"));

        Assert.Equal(ChatSenderType.Staff, reply.SenderType);
        Assert.Equal("Nhân viên Mai", reply.SenderName);
        var conversation = await Db(db => db.ChatConversations.AsNoTracking().SingleAsync());
        Assert.Equal(0, conversation.StaffUnreadCount);
        Assert.Equal(2, conversation.CustomerUnreadCount);
        Assert.Equal(Staff.UserId, conversation.AssignedStaffId);
        Assert.True(await Db(db => db.ChatMessages.Where(m => m.SenderType == ChatSenderType.Customer).AllAsync(m => m.IsRead)));
        Assert.Equal(1, await Db(db => db.Notifications.CountAsync(n => n.UserId == userId && n.Type == NotificationType.NewChatMessage)));

        // Opening the widget marks the replies as read and tells the staff side.
        var view = await Chat(c => c.GetForCustomerAsync(customer));
        Assert.Equal(0, view.Conversation!.CustomerUnreadCount);
        Assert.True(await Db(db => db.ChatMessages.Where(m => m.SenderType == ChatSenderType.Staff).AllAsync(m => m.IsRead)));
        Assert.Contains(_host.ChatEvents.Reads, r => r.ConversationId == conversationId && r.Reader == ChatSenderType.Customer);
    }

    [Fact]
    public async Task OpenForStaff_ResetsUnread_AndListShowsWaitingConversationsFirst()
    {
        var answered = await StartedGuestAsync("Đã trả lời");
        await SayAsync(answered, "Câu hỏi 1");
        var answeredId = await Db(db => db.ChatConversations.Where(c => c.CustomerName == "Đã trả lời").Select(c => c.Id).SingleAsync());
        await Chat(c => c.SendFromStaffAsync(Staff, answeredId, "Đã trả lời"));

        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var waiting = await StartedGuestAsync("Đang chờ");
        await SayAsync(waiting, "Có ai không ạ? 0987 654 321");

        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await SayAsync(answered, "Cảm ơn"); // newest, but also waiting
        await Chat(c => c.SendFromStaffAsync(Staff, answeredId, "Dạ"));

        var list = await Chat(c => c.ListForStaffAsync(new AdminChatQuery()));
        Assert.Equal("Đang chờ", list.Items[0].CustomerName); // unanswered first
        Assert.Equal(1, await Chat(c => c.CountWaitingAsync()));
        Assert.Equal("Đang chờ", Assert.Single((await Chat(c => c.ListForStaffAsync(new AdminChatQuery { UnreadOnly = true }))).Items).CustomerName);
        Assert.Equal("Đang chờ", Assert.Single((await Chat(c => c.ListForStaffAsync(new AdminChatQuery { Search = "654 321" }))).Items).CustomerName);

        var waitingId = list.Items[0].Id;
        var thread = await Chat(c => c.OpenForStaffAsync(waitingId));
        Assert.Equal(0, thread.Conversation.StaffUnreadCount);
        Assert.Equal(0, await Chat(c => c.CountWaitingAsync()));
        Assert.Contains(_host.ChatEvents.Reads, r => r.ConversationId == waitingId && r.Reader == ChatSenderType.Staff);
    }

    [Fact]
    public async Task CloseAndReopen_AddSystemMessages_AndACustomerMessageReopens()
    {
        var guest = await StartedGuestAsync();
        await SayAsync(guest, "Hỏi chút");
        var id = await Db(db => db.ChatConversations.Select(c => c.Id).SingleAsync());

        var closed = await Chat(c => c.SetStatusAsync(Staff, id, ConversationStatus.Closed));
        Assert.Equal(ConversationStatus.Closed, closed.Status);
        Assert.Contains(_host.ChatEvents.Sent, e => e.Message.SenderType == ChatSenderType.System && e.Message.Content.Contains("kết thúc"));

        await SayAsync(guest, "Cho mình hỏi thêm");
        Assert.Equal(ConversationStatus.Open, await Db(db => db.ChatConversations.Select(c => c.Status).SingleAsync()));

        await Assert.ThrowsAsync<AppValidationException>(() => Chat(c => c.SetStatusAsync(Staff, id, (ConversationStatus)99)));
        await Assert.ThrowsAsync<NotFoundException>(() => Chat(c => c.SendFromStaffAsync(Staff, 999_999, "?")));
    }

    // ------------------------------------------------------------------ content & limits

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    [InlineData(null)]
    public async Task EmptyMessages_AreRejected(string? content)
    {
        var guest = await StartedGuestAsync();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => SayAsync(guest, content!));
        Assert.True(ex.FieldErrors.ContainsKey("Content"));
    }

    [Fact]
    public async Task Content_IsStoredAsPlainText_ControlCharactersRemoved_LengthLimited()
    {
        var guest = await StartedGuestAsync();

        var message = await SayAsync(guest, "  <script>alert('x')</script>\u0007\r\n\r\n\r\n\r\nDòng 2  ");
        Assert.Equal("<script>alert('x')</script>\n\nDòng 2", message.Content); // kept literally; the UI renders it as text

        await Assert.ThrowsAsync<AppValidationException>(() => SayAsync(guest, new string('a', 2001)));
        Assert.Equal(2000, (await SayAsync(guest, new string('b', 2000))).Content.Length);
    }

    [Fact]
    public async Task CustomerRateLimit_BlocksFloods()
    {
        var guest = await StartedGuestAsync();
        for (var i = 0; i < ChatService.CustomerMessagesPerMinute; i++)
        {
            await SayAsync(guest, $"Tin {i}");
        }

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => SayAsync(guest, "Thêm nữa"));
        Assert.Contains("quá nhanh", ex.Message);

        _host.Clock.Advance(TimeSpan.FromSeconds(61));
        await SayAsync(guest, "Sau một phút gửi được");
    }

    [Fact]
    public async Task History_IsPaged_OldestFirst()
    {
        var guest = await StartedGuestAsync();
        for (var i = 1; i <= 35; i++)
        {
            await SayAsync(guest, $"Tin {i}");
            _host.Clock.Advance(TimeSpan.FromSeconds(5)); // stay under the rate limit
        }

        var first = await Chat(c => c.GetForCustomerAsync(guest));
        Assert.Equal(30, first.Messages.Count);
        Assert.True(first.HasMore);
        Assert.Equal("Tin 6", first.Messages[0].Content);
        Assert.Equal("Tin 35", first.Messages[^1].Content);

        var older = await Chat(c => c.GetOlderForCustomerAsync(guest, first.Messages[0].Id));
        Assert.Equal(["Tin 1", "Tin 2", "Tin 3", "Tin 4", "Tin 5"], older.Select(m => m.Content));
    }

    // ------------------------------------------------------------------ sign-in

    [Fact]
    public async Task GuestConversation_FollowsTheCustomerAfterSignIn_UnlessTheyAlreadyHaveOne()
    {
        var guest = await StartedGuestAsync("Khách lạ");
        await SayAsync(guest, "Tin nhắn trước khi đăng nhập");
        var userId = await AdminServiceTests.CreateCustomerAsync(_host);

        Assert.True(await Chat(c => c.ClaimGuestConversationAsync(guest.GuestKey!, userId, "Anh Minh")));

        var mine = await Chat(c => c.GetForCustomerAsync(new ChatCustomer(userId, null)));
        Assert.Equal("Anh Minh", mine.Conversation!.CustomerName);
        Assert.Equal("Tin nhắn trước khi đăng nhập", Assert.Single(mine.Messages).Content);
        Assert.Null((await Chat(c => c.GetForCustomerAsync(guest))).Conversation); // the guest key no longer opens it

        var otherGuest = await StartedGuestAsync();
        Assert.False(await Chat(c => c.ClaimGuestConversationAsync(otherGuest.GuestKey!, userId, "Anh Minh")));
    }
}
