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

    /// <summary>Small line under the name in the logo ("Furniture"); split off the name when the name ends with it.</summary>
    public string? LogoSubtitle { get; set; }

    /// <summary>Uploaded logo picture (resized); empty: the built-in house icon.</summary>
    public string? LogoUrl { get; set; }

    public int? LogoWidth { get; set; }
    public int? LogoHeight { get; set; }

    /// <summary>The picture already contains the store name: the name text next to it is hidden.</summary>
    public bool LogoShowsName { get; set; }

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

/// <summary>
/// The banner at the top of the home page, editable by admins (single row, table HomeBanners).
/// No row means the built-in banner. Empty optional texts hide their element (button, statistic...).
/// </summary>
public class HomeBanner : AuditableEntity
{
    public string? Eyebrow { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>End of the title shown in italics and wood colour ("màu óc chó").</summary>
    public string? TitleHighlight { get; set; }

    public string? Description { get; set; }

    public string? PrimaryButtonText { get; set; }

    /// <summary>Empty: the main product line (ApplicationSettings:FocusCategorySlug).</summary>
    public string? PrimaryButtonUrl { get; set; }

    public string? SecondaryButtonText { get; set; }
    public string? SecondaryButtonUrl { get; set; }

    public string? Stat1Value { get; set; }
    public string? Stat1Label { get; set; }
    public string? Stat2Value { get; set; }
    public string? Stat2Label { get; set; }
    public string? Stat3Value { get; set; }
    public string? Stat3Label { get; set; }

    /// <summary>Uploaded picture (resized); empty: the built-in dining room illustration.</summary>
    public string? ImageUrl { get; set; }

    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public string? ImageAlt { get; set; }
}
