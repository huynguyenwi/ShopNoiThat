using System.ComponentModel.DataAnnotations;

namespace FurnitureStore.Application.Common.Settings;

/// <summary>
/// General site settings bound from the "ApplicationSettings" configuration section.
/// </summary>
public sealed class ApplicationSettings
{
    public const string SectionName = "ApplicationSettings";

    [Required]
    public string SiteName { get; set; } = "Nhà Mộc Furniture";

    public string Tagline { get; set; } = "Bàn ghế ăn gỗ sồi Nga - Đóng tại xưởng";

    /// <summary>Public base URL, used for absolute links (sitemap, Open Graph).</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "https://localhost:7160";

    [Range(4, 100)]
    public int DefaultPageSize { get; set; } = 12;

    /// <summary>
    /// Category of the store's main product line ("bo-ban-an": dining sets): first section of the home page and a link in
    /// the main menu. Empty, or a category that does not exist, hides both.
    /// </summary>
    public string? FocusCategorySlug { get; set; } = "bo-ban-an";

    [Required]
    public StoreSettings Store { get; set; } = new();
}

/// <summary>
/// Default store / workshop information. Seeded into the database later so Admin can edit it.
/// </summary>
public sealed class StoreSettings
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string WorkshopAddress { get; set; } = string.Empty;
    public string Hotline { get; set; } = string.Empty;
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    public string OpeningHours { get; set; } = string.Empty;
    public string? FacebookUrl { get; set; }
    public string? TikTokUrl { get; set; }
    public string? ZaloUrl { get; set; }
    public string? GoogleMapsEmbedUrl { get; set; }
}
