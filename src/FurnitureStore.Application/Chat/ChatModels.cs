using FurnitureStore.Application.Common.Models;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Chat;

/// <summary>
/// The customer side of a conversation: a signed-in user (<see cref="UserId"/>) or a guest identified by the random key
/// stored in their chat cookie (<see cref="GuestKey"/>).
/// </summary>
public sealed record ChatCustomer(string? UserId, string? GuestKey, string? DisplayName = null, string? Email = null, string? Phone = null)
{
    public bool IsKnown => UserId is not null || GuestKey is not null;
}

/// <summary>A shop employee (ADMIN or STAFF) answering chats.</summary>
public sealed record ChatStaff(string UserId, string DisplayName);

public sealed record ChatConversationDto(
    int Id,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    string? CustomerId,
    bool IsGuest,
    int? ProductId,
    string? ProductName,
    string? ProductSlug,
    ConversationStatus Status,
    DateTime CreatedAt,
    DateTime LastMessageAt,
    string? LastMessagePreview,
    int CustomerUnreadCount,
    int StaffUnreadCount,
    string? AssignedStaffId);

public sealed record ChatMessageDto(
    int Id,
    int ConversationId,
    ChatSenderType SenderType,
    string SenderName,
    string Content,
    DateTime SentAt,
    bool IsRead);

/// <summary>What the chat widget needs: the customer's conversation (null before the first message) and its latest messages.</summary>
public sealed record CustomerChatDto(ChatConversationDto? Conversation, IReadOnlyList<ChatMessageDto> Messages, bool HasMore, bool RequiresContactInfo);

public sealed record StaffThreadDto(ChatConversationDto Conversation, IReadOnlyList<ChatMessageDto> Messages, bool HasMore);

/// <summary>Contact details a guest gives before chatting (signed-in customers use their profile).</summary>
public sealed class StartChatCommand
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int? ProductId { get; set; }
}

public sealed class SendChatMessageCommand
{
    public string? Content { get; set; }

    /// <summary>Product page the customer is on (only used when a new conversation is created).</summary>
    public int? ProductId { get; set; }
}

public sealed class AdminChatQuery
{
    public string? Search { get; set; }
    public ConversationStatus? Status { get; set; }
    public bool UnreadOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}

/// <summary>
/// Pushes chat events to connected clients (implemented with SignalR in the Web project).
/// <paramref name="customer"/> identifies the customer's own connections; it is used for routing only and never sent to clients.
/// </summary>
public interface IChatNotifier
{
    /// <summary>A new message: delivered to the customer's connections and to all staff.</summary>
    Task MessageSentAsync(ChatCustomer customer, ChatConversationDto conversation, ChatMessageDto message, CancellationToken cancellationToken = default);

    /// <summary>One side read the other side's messages.</summary>
    Task MessagesReadAsync(ChatCustomer customer, ChatConversationDto conversation, ChatSenderType reader, CancellationToken cancellationToken = default);
}

public interface IChatRepository : Common.Interfaces.IRepository<ChatConversation>
{
    /// <summary>The customer's most recent conversation (open or closed).</summary>
    Task<ChatConversation?> FindLatestForCustomerAsync(ChatCustomer customer, CancellationToken cancellationToken = default);

    Task<ChatConversationDto?> GetDtoAsync(int conversationId, CancellationToken cancellationToken = default);

    /// <summary>Messages older than <paramref name="beforeMessageId"/> (or the latest ones), returned oldest first.</summary>
    Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(int conversationId, int? beforeMessageId, int take, CancellationToken cancellationToken = default);

    Task<PagedResult<ChatConversationDto>> SearchAsync(AdminChatQuery query, CancellationToken cancellationToken = default);

    Task<int> CountRecentMessagesAsync(int conversationId, ChatSenderType senderType, DateTime sinceUtc, CancellationToken cancellationToken = default);

    Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);

    /// <summary>Marks the messages written by <paramref name="writer"/> as read. Returns the number of rows changed.</summary>
    Task<int> MarkReadAsync(int conversationId, ChatSenderType writer, DateTime readAtUtc, CancellationToken cancellationToken = default);

    Task<int> CountConversationsWaitingForStaffAsync(CancellationToken cancellationToken = default);
}
