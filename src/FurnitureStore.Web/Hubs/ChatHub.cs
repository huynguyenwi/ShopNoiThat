using FurnitureStore.Application.Chat;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Domain.Exceptions;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FurnitureStore.Web.Hubs;

/// <summary>Messages the server pushes to browsers (method names are the JavaScript event names).</summary>
public interface IChatClient
{
    Task ReceiveMessage(ChatMessageDto message, ChatConversationDto conversation);

    Task MessagesRead(int conversationId, ChatSenderType reader);

    Task Typing(int conversationId, string name, bool fromStaff);
}

/// <summary>
/// Realtime customer ↔ shop chat at /hubs/chat. Guests and customers join their own group; ADMIN / STAFF join the "staff"
/// group and see every conversation. All writes go through <see cref="IChatService"/>, which validates and rate-limits.
/// </summary>
[AllowAnonymous]
public sealed class ChatHub(IChatService chat, ILogger<ChatHub> logger) : Hub<IChatClient>
{
    public const string Path = "/hubs/chat";
    public const string StaffGroup = "staff";

    /// <summary>Group of a customer's connections. Guest keys are hashed so the group name never reveals the cookie value.</summary>
    public static string CustomerGroup(ChatCustomer customer) =>
        customer.UserId is not null ? $"user:{customer.UserId}" : $"guest:{Hash(customer.GuestKey!)}";

    private HttpContext Http => Context.GetHttpContext() ?? throw new HubException("Kết nối không hợp lệ.");

    public override async Task OnConnectedAsync()
    {
        var http = Http;
        if (!IsSameOrigin(http))
        {
            // Cross-site WebSocket hijacking protection: only pages of this site may connect.
            logger.LogWarning("Rejected chat connection from origin {Origin}", http.Request.Headers.Origin.ToString());
            Context.Abort();
            return;
        }

        if (ChatIdentity.IsStaff(Context.User!))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, StaffGroup);
        }

        var customer = ChatIdentity.Resolve(http);
        if (customer.IsKnown)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, CustomerGroup(customer));
        }

        await base.OnConnectedAsync();
    }

    // ------------------------------------------------------------------ customer

    public Task<ChatMessageDto> SendMessage(string? content, int? productId) =>
        Guard(() => chat.SendFromCustomerAsync(ChatIdentity.Resolve(Http), new SendChatMessageCommand { Content = content, ProductId = productId }, Context.ConnectionAborted));

    public Task MarkRead() =>
        Guard(async () => { await chat.MarkReadByCustomerAsync(ChatIdentity.Resolve(Http), Context.ConnectionAborted); return true; });

    public async Task Typing()
    {
        var customer = ChatIdentity.Resolve(Http);
        var conversation = await chat.FindForCustomerAsync(customer, Context.ConnectionAborted);
        if (conversation is not null)
        {
            await Clients.Group(StaffGroup).Typing(conversation.Id, conversation.CustomerName, false);
        }
    }

    // ------------------------------------------------------------------ staff

    [Authorize(Policy = AuthorizationPolicies.BackOffice)]
    public Task<ChatMessageDto> SendStaffMessage(int conversationId, string? content) =>
        Guard(() => chat.SendFromStaffAsync(Staff(), conversationId, content, Context.ConnectionAborted));

    [Authorize(Policy = AuthorizationPolicies.BackOffice)]
    public Task MarkReadByStaff(int conversationId) =>
        Guard(async () => { await chat.MarkReadByStaffAsync(conversationId, Context.ConnectionAborted); return true; });

    [Authorize(Policy = AuthorizationPolicies.BackOffice)]
    public async Task StaffTyping(int conversationId)
    {
        var customer = await Guard(() => chat.GetCustomerOfAsync(conversationId, Context.ConnectionAborted));
        await Clients.Group(CustomerGroup(customer)).Typing(conversationId, Staff().DisplayName, true);
    }

    private ChatStaff Staff() => new(Context.User!.UserId()!, Context.User!.DisplayName());

    /// <summary>Business errors become a HubException whose (Vietnamese) message reaches the client; other errors stay generic.</summary>
    private async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (AppValidationException ex)
        {
            throw new HubException(string.Join(" ", ex.Errors));
        }
        catch (Exception ex) when (ex is BusinessRuleException or NotFoundException or ForbiddenAccessException or DomainException)
        {
            throw new HubException(ex.Message);
        }
    }

    private static bool IsSameOrigin(HttpContext http)
    {
        var origin = http.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return true; // same-origin XHR / SSE requests do not always send Origin
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && string.Equals(uri.Authority, http.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..24];
}
