using System.ComponentModel.DataAnnotations;
using FurnitureStore.Application.Chat;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FurnitureStore.Web.Controllers.Api;

public sealed class ChatStatusRequest
{
    [Required]
    public ConversationStatus? Status { get; set; }
}

public sealed class StaffMessageRequest
{
    public string? Content { get; set; }
}

/// <summary>
/// Customer side of the chat (guests included). Realtime delivery happens over SignalR (/hubs/chat);
/// these endpoints load history, register a guest and send messages when WebSockets are unavailable.
/// </summary>
[Route("api/chat")]
public sealed class ChatApiController(IChatService chat, TimeProvider timeProvider) : ApiControllerBase
{
    /// <summary>GET /api/chat - the visitor's conversation and latest messages (marks the shop's replies as read).</summary>
    [HttpGet("")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var customer = ChatIdentity.Resolve(HttpContext);
        var result = await chat.GetForCustomerAsync(customer, cancellationToken);
        return OkResponse(new
        {
            signedIn = customer.UserId is not null,
            displayName = customer.DisplayName,
            result.Conversation,
            result.Messages,
            result.HasMore,
            result.RequiresContactInfo
        });
    }

    /// <summary>GET /api/chat/unread - unread replies for the launcher badge (does not mark anything as read).</summary>
    [HttpGet("unread")]
    public async Task<IActionResult> Unread(CancellationToken cancellationToken)
    {
        var conversation = await chat.FindForCustomerAsync(ChatIdentity.Resolve(HttpContext), cancellationToken);
        return OkResponse(new { hasConversation = conversation is not null, count = conversation?.CustomerUnreadCount ?? 0 });
    }

    /// <summary>POST /api/chat/start { name, phone, email, productId } - guests give contact details before chatting.</summary>
    [HttpPost("start")]
    [EnableRateLimiting(RateLimitPolicies.Forms)]
    public async Task<IActionResult> Start([FromBody] StartChatCommand command, CancellationToken cancellationToken)
    {
        var customer = ChatIdentity.Resolve(HttpContext);
        if (!customer.IsKnown)
        {
            customer = ChatIdentity.EnsureGuest(HttpContext, timeProvider);
        }

        return OkResponse(await chat.StartAsync(customer, command, cancellationToken), "Đã bắt đầu cuộc trò chuyện.");
    }

    /// <summary>GET /api/chat/conversations - the visitor's conversation with the shop (0 or 1 item).</summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations(CancellationToken cancellationToken)
    {
        var conversation = await chat.FindForCustomerAsync(ChatIdentity.Resolve(HttpContext), cancellationToken);
        return OkResponse(conversation is null ? Array.Empty<ChatConversationDto>() : [conversation with { AssignedStaffId = null }]);
    }

    /// <summary>POST /api/chat or POST /api/chat/messages { content, productId }</summary>
    [HttpPost("")]
    [HttpPost("messages")]
    public async Task<IActionResult> Send([FromBody] SendChatMessageCommand command, CancellationToken cancellationToken) =>
        OkResponse(await chat.SendFromCustomerAsync(ChatIdentity.Resolve(HttpContext), command, cancellationToken), "Đã gửi tin nhắn.");

    /// <summary>GET /api/chat/messages?before={messageId} - older messages.</summary>
    [HttpGet("messages")]
    public async Task<IActionResult> Older([FromQuery] int before, CancellationToken cancellationToken) =>
        OkResponse(await chat.GetOlderForCustomerAsync(ChatIdentity.Resolve(HttpContext), before, cancellationToken));

    /// <summary>POST /api/chat/read</summary>
    [HttpPost("read")]
    public async Task<IActionResult> Read(CancellationToken cancellationToken)
    {
        await chat.MarkReadByCustomerAsync(ChatIdentity.Resolve(HttpContext), cancellationToken);
        return OkResponse();
    }
}

/// <summary>Staff side of the chat: ADMIN and STAFF only (checked here, not just hidden in the UI).</summary>
[Route("api/admin/chat")]
[Authorize(Policy = AuthorizationPolicies.BackOffice)]
public sealed class AdminChatApiController(IChatService chat) : ApiControllerBase
{
    /// <summary>GET /api/admin/chat/conversations?status=Open&amp;search=&amp;unreadOnly=true&amp;page=1</summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> List([FromQuery] AdminChatQuery query, CancellationToken cancellationToken) =>
        OkResponse(await chat.ListForStaffAsync(query, cancellationToken));

    /// <summary>GET /api/admin/chat/conversations/{id} - opens the thread and marks the customer's messages as read.</summary>
    [HttpGet("conversations/{id:int}")]
    public async Task<IActionResult> Open(int id, CancellationToken cancellationToken) =>
        OkResponse(await chat.OpenForStaffAsync(id, cancellationToken));

    [HttpGet("conversations/{id:int}/messages")]
    public async Task<IActionResult> Older(int id, [FromQuery] int before, CancellationToken cancellationToken) =>
        OkResponse(await chat.GetOlderForStaffAsync(id, before, cancellationToken));

    [HttpPost("conversations/{id:int}/messages")]
    public async Task<IActionResult> Send(int id, [FromBody] StaffMessageRequest request, CancellationToken cancellationToken) =>
        OkResponse(await chat.SendFromStaffAsync(Staff(), id, request.Content, cancellationToken), "Đã gửi tin nhắn.");

    [HttpPost("conversations/{id:int}/read")]
    public async Task<IActionResult> Read(int id, CancellationToken cancellationToken)
    {
        await chat.MarkReadByStaffAsync(id, cancellationToken);
        return OkResponse();
    }

    [HttpPost("conversations/{id:int}/status")]
    public async Task<IActionResult> Status(int id, [FromBody] ChatStatusRequest request, CancellationToken cancellationToken) =>
        OkResponse(await chat.SetStatusAsync(Staff(), id, request.Status!.Value, cancellationToken),
            request.Status == ConversationStatus.Closed ? "Đã kết thúc cuộc trò chuyện." : "Đã mở lại cuộc trò chuyện.");

    /// <summary>GET /api/admin/chat/waiting-count - conversations with unanswered customer messages.</summary>
    [HttpGet("waiting-count")]
    public async Task<IActionResult> WaitingCount(CancellationToken cancellationToken) =>
        OkResponse(new { count = await chat.CountWaitingAsync(cancellationToken) });

    private ChatStaff Staff() => new(User.UserId()!, User.DisplayName());
}
