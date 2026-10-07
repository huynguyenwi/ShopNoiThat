using FurnitureStore.Application.Common.Models;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Engagement;

// ------------------------------------------------------------------ Reviews

public sealed record ReviewDto(
    int Id,
    int ProductId,
    string ReviewerName,
    int Rating,
    string? Title,
    string Comment,
    bool IsVerifiedPurchase,
    DateTime CreatedAt,
    string? AdminReply,
    DateTime? AdminRepliedAt,
    IReadOnlyList<string> ImageUrls);

/// <summary>Distribution[i] = number of (i + 1)-star reviews.</summary>
public sealed record ReviewStatsDto(decimal Average, int Count, IReadOnlyList<int> Distribution);

public sealed record ProductReviewsDto(ReviewStatsDto Stats, PagedResult<ReviewDto> Reviews);

public sealed record ReviewEligibilityDto(bool CanReview, string? Reason, ReviewDto? Existing);

public sealed class ReviewCommand
{
    public int Rating { get; set; } = 5;
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public sealed record AdminReviewDto(
    int Id,
    int ProductId,
    string ProductName,
    string ProductSlug,
    string UserId,
    string ReviewerName,
    int Rating,
    string? Title,
    string Comment,
    bool IsHidden,
    bool IsVerifiedPurchase,
    DateTime CreatedAt,
    string? AdminReply,
    IReadOnlyList<string> ImageUrls);

public sealed class AdminReviewQuery
{
    public string? Search { get; set; }
    public int? Rating { get; set; }
    public bool? Hidden { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record HomeReviewDto(string ReviewerName, int Rating, string Comment, string ProductName, string ProductSlug, DateTime CreatedAt);

public interface IReviewRepository : Common.Interfaces.IRepository<Domain.Entities.Review>
{
    Task<PagedResult<ReviewDto>> GetVisibleForProductAsync(int productId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ReviewStatsDto> GetStatsAsync(int productId, CancellationToken cancellationToken = default);
    Task<Domain.Entities.Review?> GetByUserAndProductAsync(string userId, int productId, CancellationToken cancellationToken = default);
    Task<Domain.Entities.Review?> GetWithImagesAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminReviewDto>> SearchAdminAsync(AdminReviewQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HomeReviewDto>> GetLatestAsync(int take, CancellationToken cancellationToken = default);
    Task UpdateProductRatingAsync(int productId, decimal average, int count, CancellationToken cancellationToken = default);
}

// ------------------------------------------------------------------ Contact

public sealed class ContactCommand
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed record ContactMessageDto(
    int Id,
    string FullName,
    string Phone,
    string Email,
    string? Subject,
    string Message,
    ContactMessageStatus Status,
    string? AdminNote,
    string? IpAddress,
    DateTime CreatedAt,
    DateTime? RepliedAt);

public interface IContactRepository : Common.Interfaces.IRepository<Domain.Entities.ContactMessage>
{
    Task<PagedResult<ContactMessageDto>> SearchAsync(ContactMessageStatus? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> CountNewAsync(CancellationToken cancellationToken = default);
}

// ------------------------------------------------------------------ Store information

public sealed record StoreInfoDto(
    string Name,
    string? LogoSubtitle,
    string? Tagline,
    string? About,
    string Address,
    string? WorkshopAddress,
    string Hotline,
    string Email,
    string? OpeningHours,
    string? FacebookUrl,
    string? TikTokUrl,
    string? ZaloUrl,
    string? GoogleMapsEmbedUrl)
{
    public string HotlineDigits => new(Hotline.Where(c => char.IsDigit(c) || c == '+').ToArray());

    /// <summary>
    /// Big line of the logo, also the short name in texts such as "Chat với ...": the name without its logo subtitle when
    /// it ends with it ("Nhà Mộc Furniture" + "Furniture" → "Nhà Mộc"), otherwise the whole name.
    /// </summary>
    public string BrandName => BrandNameOf(Name, LogoSubtitle);

    /// <summary>See <see cref="BrandName"/> (same rule as the live preview in wwwroot/js/admin-store.js).</summary>
    public static string BrandNameOf(string name, string? logoSubtitle)
    {
        name = name.Trim();
        var subtitle = logoSubtitle?.Trim();
        if (string.IsNullOrEmpty(subtitle) || !name.EndsWith(" " + subtitle, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        var shortName = name[..^(subtitle.Length + 1)].TrimEnd();
        return shortName.Length == 0 ? name : shortName;
    }
}

public sealed class StoreInfoCommand
{
    public string Name { get; set; } = string.Empty;
    public string? LogoSubtitle { get; set; }
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
