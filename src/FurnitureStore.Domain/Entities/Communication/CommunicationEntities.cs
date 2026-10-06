using FurnitureStore.Domain.Common;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Domain.Entities;

/// <summary>Customer ↔ shop conversation (realtime chat via SignalR).</summary>
public class ChatConversation : AuditableEntity
{
    /// <summary>Signed-in customer; null for guests.</summary>
    public string? CustomerId { get; set; }

    /// <summary>Random key stored in the guest's cookie so they can resume the conversation.</summary>
    public string? GuestKey { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    public string? AssignedStaffId { get; set; }

    /// <summary>Product the customer was looking at when opening the chat.</summary>
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public string? Subject { get; set; }
    public ConversationStatus Status { get; set; } = ConversationStatus.Open;

    public DateTime LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public int CustomerUnreadCount { get; set; }
    public int StaffUnreadCount { get; set; }
    public DateTime? ClosedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}

public class ChatMessage : BaseEntity
{
    public const int MaxContentLength = 2000;

    public int ConversationId { get; set; }
    public ChatConversation Conversation { get; set; } = null!;

    public string? SenderId { get; set; }
    public ChatSenderType SenderType { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}

/// <summary>Message sent from the contact form.</summary>
public class ContactMessage : AuditableEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
    public ContactMessageStatus Status { get; set; } = ContactMessageStatus.New;
    public string? UserId { get; set; }
    public string? IpAddress { get; set; }
    public string? AdminNote { get; set; }
    public DateTime? RepliedAt { get; set; }
}

/// <summary>
/// In-app notification. Sent to a single user (<see cref="UserId"/>) or to everyone in a role
/// (<see cref="RecipientRole"/>, e.g. all admins for a new order).
/// </summary>
public class Notification : BaseEntity
{
    public string? UserId { get; set; }
    public string? RecipientRole { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
