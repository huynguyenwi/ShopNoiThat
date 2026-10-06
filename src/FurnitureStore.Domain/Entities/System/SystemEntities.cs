using FurnitureStore.Domain.Common;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Domain.Entities;

/// <summary>Who did what, when (admin changes, logins, order status changes...).</summary>
public class AuditLog : BaseEntity
{
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public AuditAction Action { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }

    /// <summary>JSON of changed properties before the change. Sensitive fields are never recorded.</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON of changed properties after the change.</summary>
    public string? NewValues { get; set; }

    public string? Description { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Store / workshop information editable by admins (single row, table StoreInformation).</summary>
public class StoreInfo : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Tagline { get; set; }
    public string? About { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? WorkshopAddress { get; set; }
    public string Hotline { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? OpeningHours { get; set; }
    public string? FacebookUrl { get; set; }
    public string? TikTokUrl { get; set; }
    public string? ZaloUrl { get; set; }
    public string? GoogleMapsEmbedUrl { get; set; }
}
