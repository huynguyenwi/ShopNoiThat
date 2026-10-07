using FluentValidation;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Media;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Application.Engagement;

// ================================================================== Reviews

public interface IReviewService
{
    Task<ProductReviewsDto> GetForProductAsync(int productId, int page = 1, CancellationToken cancellationToken = default);
    Task<ReviewEligibilityDto> GetEligibilityAsync(string? userId, int productId, CancellationToken cancellationToken = default);
    Task<ReviewDto> SubmitAsync(string userId, string reviewerName, int productId, ReviewCommand command, IReadOnlyList<(Stream Content, string FileName)> images, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HomeReviewDto>> GetLatestAsync(int take = 6, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminReviewDto>> SearchAsync(AdminReviewQuery query, CancellationToken cancellationToken = default);
    Task SetHiddenAsync(int reviewId, bool hidden, CancellationToken cancellationToken = default);
    Task ReplyAsync(int reviewId, string? reply, CancellationToken cancellationToken = default);
    Task DeleteAsync(int reviewId, CancellationToken cancellationToken = default);
}

public sealed class ReviewCommandValidator : AbstractValidator<ReviewCommand>
{
    public ReviewCommandValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(Review.MinRating, Review.MaxRating).WithMessage("Vui lòng chọn từ 1 đến 5 sao.");
        RuleFor(x => x.Title).MaximumLength(200).WithMessage("Tiêu đề tối đa 200 ký tự.");
        RuleFor(x => x.Comment).NotEmpty().WithMessage("Vui lòng viết nhận xét.")
            .Length(10, 2000).WithMessage("Nhận xét từ 10 đến 2.000 ký tự.");
    }
}

public sealed class ReviewService(
    IReviewRepository reviews,
    IRepository<ReviewImage> reviewImages,
    IProductRepository products,
    IOrderRepository orders,
    IFileStorageService fileStorage,
    INotificationService notifications,
    IValidator<ReviewCommand> validator,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLog,
    CatalogCache catalogCache,
    TimeProvider timeProvider,
    ILogger<ReviewService> logger) : IReviewService
{
    public const int MaxImages = 3;
    public const int PageSize = 5;

    public async Task<ProductReviewsDto> GetForProductAsync(int productId, int page = 1, CancellationToken cancellationToken = default) =>
        new(await reviews.GetStatsAsync(productId, cancellationToken),
            await reviews.GetVisibleForProductAsync(productId, Math.Max(1, page), PageSize, cancellationToken));

    public async Task<ReviewEligibilityDto> GetEligibilityAsync(string? userId, int productId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return new ReviewEligibilityDto(false, "Đăng nhập để viết đánh giá.", null);
        }

        var existing = await reviews.GetByUserAndProductAsync(userId, productId, cancellationToken);
        if (await orders.FindDeliveredItemAsync(userId, productId, cancellationToken) is null)
        {
            return new ReviewEligibilityDto(false, "Chỉ khách hàng đã mua và nhận sản phẩm mới có thể đánh giá.", existing is null ? null : Map(existing));
        }

        return new ReviewEligibilityDto(true, null, existing is null ? null : Map(existing));
    }

    public async Task<ReviewDto> SubmitAsync(string userId, string reviewerName, int productId, ReviewCommand command,
        IReadOnlyList<(Stream Content, string FileName)> images, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(command, cancellationToken);
        if (images.Count > MaxImages)
        {
            throw new AppValidationException($"Tối đa {MaxImages} ảnh cho mỗi đánh giá.");
        }

        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null || product.Status != ProductStatus.Active)
        {
            throw new NotFoundException("sản phẩm", productId);
        }

        var purchase = await orders.FindDeliveredItemAsync(userId, productId, cancellationToken)
                       ?? throw new ForbiddenAccessException("Chỉ khách hàng đã mua và nhận sản phẩm mới có thể đánh giá.");

        var stored = new List<StoredFile>();
        try
        {
            foreach (var (content, fileName) in images)
            {
                stored.Add(await fileStorage.SaveImageAsync(content, fileName, Common.Media.ImagePreset.Review, cancellationToken));
            }

            var review = await reviews.GetByUserAndProductAsync(userId, productId, cancellationToken);
            var isNew = review is null;
            if (review is null)
            {
                review = new Review { ProductId = productId, UserId = userId };
                await reviews.AddAsync(review, cancellationToken);
            }

            review.ReviewerName = string.IsNullOrWhiteSpace(reviewerName) ? "Khách hàng" : reviewerName.Trim();
            review.OrderItemId = purchase.Id;
            review.IsVerifiedPurchase = true;
            review.Rating = command.Rating;
            review.Title = string.IsNullOrWhiteSpace(command.Title) ? null : command.Title.Trim();
            review.Comment = command.Comment.Trim();

            var replacedUrls = new List<string>();
            if (stored.Count > 0)
            {
                foreach (var old in review.Images.ToList())
                {
                    review.Images.Remove(old);
                    reviewImages.Remove(old);
                    replacedUrls.Add(old.Url);
                }

                for (var i = 0; i < stored.Count; i++)
                {
                    review.Images.Add(new ReviewImage { Url = stored[i].Url, DisplayOrder = i });
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            stored.Clear(); // committed: the new files now belong to the review

            // Old files are removed only after the database no longer references them.
            foreach (var url in replacedUrls)
            {
                await fileStorage.DeleteAsync(url, CancellationToken.None);
            }

            await RecalculateAsync(productId, cancellationToken);

            if (isNew)
            {
                await notifications.NotifyRoleAsync(AppRoles.Admin, NotificationType.NewReview, $"Đánh giá mới {command.Rating}★",
                    $"{review.ReviewerName} đánh giá \"{product.Name}\"", "/admin/reviews", cancellationToken);
            }

            logger.LogInformation("Review {ReviewId} for product {ProductId} saved by user {UserId}", review.Id, productId, userId);
            return Map(review);
        }
        catch
        {
            foreach (var file in stored)
            {
                await fileStorage.DeleteAsync(file.Url, CancellationToken.None);
            }
            throw;
        }
    }

    public Task<IReadOnlyList<HomeReviewDto>> GetLatestAsync(int take = 6, CancellationToken cancellationToken = default) =>
        reviews.GetLatestAsync(Math.Clamp(take, 1, 20), cancellationToken);

    public Task<PagedResult<AdminReviewDto>> SearchAsync(AdminReviewQuery query, CancellationToken cancellationToken = default)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 5, 100);
        return reviews.SearchAdminAsync(query, cancellationToken);
    }

    public async Task SetHiddenAsync(int reviewId, bool hidden, CancellationToken cancellationToken = default)
    {
        var review = await reviews.GetByIdAsync(reviewId, cancellationToken) ?? throw new NotFoundException("đánh giá", reviewId);
        review.IsHidden = hidden;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await RecalculateAsync(review.ProductId, cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(Review), reviewId.ToString(), hidden ? "Ẩn đánh giá" : "Hiện đánh giá"), cancellationToken);
    }

    public async Task ReplyAsync(int reviewId, string? reply, CancellationToken cancellationToken = default)
    {
        var review = await reviews.GetByIdAsync(reviewId, cancellationToken) ?? throw new NotFoundException("đánh giá", reviewId);
        var text = string.IsNullOrWhiteSpace(reply) ? null : reply.Trim();
        if (text?.Length > 2000)
        {
            throw new AppValidationException("Phản hồi tối đa 2.000 ký tự.");
        }

        review.AdminReply = text;
        review.AdminRepliedAt = text is null ? null : timeProvider.GetUtcNow().UtcDateTime;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int reviewId, CancellationToken cancellationToken = default)
    {
        var review = await reviews.GetWithImagesAsync(reviewId, cancellationToken) ?? throw new NotFoundException("đánh giá", reviewId);
        var urls = review.Images.Select(i => i.Url).ToList();
        reviews.Remove(review);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var url in urls)
        {
            await fileStorage.DeleteAsync(url, cancellationToken);
        }

        await RecalculateAsync(review.ProductId, cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Delete, nameof(Review), reviewId.ToString(), $"Xóa đánh giá của {review.ReviewerName}",
            OldValues: new { review.Rating, review.Comment }), cancellationToken);
    }

    private async Task RecalculateAsync(int productId, CancellationToken cancellationToken)
    {
        var stats = await reviews.GetStatsAsync(productId, cancellationToken);
        await reviews.UpdateProductRatingAsync(productId, stats.Average, stats.Count, cancellationToken);
        catalogCache.Invalidate();
    }

    private static ReviewDto Map(Review r) => new(
        r.Id, r.ProductId, r.ReviewerName, r.Rating, r.Title, r.Comment, r.IsVerifiedPurchase, r.CreatedAt, r.AdminReply, r.AdminRepliedAt,
        r.Images.OrderBy(i => i.DisplayOrder).Select(i => i.Url).ToList());
}

// ================================================================== Contact

public interface IContactService
{
    Task<int> SubmitAsync(ContactCommand command, string? userId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<PagedResult<ContactMessageDto>> ListAsync(ContactMessageStatus? status, string? search, int page = 1, CancellationToken cancellationToken = default);
    Task<ContactMessageDto> OpenAsync(int id, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, ContactMessageStatus status, string? adminNote, CancellationToken cancellationToken = default);
    Task<int> CountNewAsync(CancellationToken cancellationToken = default);
}

public sealed class ContactCommandValidator : AbstractValidator<ContactCommand>
{
    public ContactCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Vui lòng nhập họ tên.").MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Vui lòng nhập số điện thoại.")
            .Matches(ValidationPatterns.VietnamesePhone).WithMessage(ValidationPatterns.VietnamesePhoneMessage);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Vui lòng nhập email.").EmailAddress().WithMessage("Email không hợp lệ.").MaximumLength(256);
        RuleFor(x => x.Subject).MaximumLength(200);
        RuleFor(x => x.Message).NotEmpty().WithMessage("Vui lòng nhập nội dung.")
            .Length(10, 4000).WithMessage("Nội dung từ 10 đến 4.000 ký tự.");
    }
}

public sealed class ContactService(
    IContactRepository messages,
    INotificationService notifications,
    IValidator<ContactCommand> validator,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ContactService> logger) : IContactService
{
    public async Task<int> SubmitAsync(ContactCommand command, string? userId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(command, cancellationToken);
        var message = new ContactMessage
        {
            FullName = command.FullName.Trim(),
            Phone = command.Phone.Trim(),
            Email = command.Email.Trim(),
            Subject = string.IsNullOrWhiteSpace(command.Subject) ? null : command.Subject.Trim(),
            Message = command.Message.Trim(),
            UserId = userId,
            IpAddress = ipAddress?[..Math.Min(ipAddress.Length, 45)],
            Status = ContactMessageStatus.New
        };

        await messages.AddAsync(message, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await notifications.NotifyRoleAsync(AppRoles.Admin, NotificationType.NewContactMessage, $"Liên hệ mới từ {message.FullName}",
            message.Subject ?? message.Message[..Math.Min(message.Message.Length, 120)], $"/admin/contacts/details/{message.Id}", cancellationToken);
        logger.LogInformation("Contact message {MessageId} received", message.Id);
        return message.Id;
    }

    public Task<PagedResult<ContactMessageDto>> ListAsync(ContactMessageStatus? status, string? search, int page = 1, CancellationToken cancellationToken = default) =>
        messages.SearchAsync(status, string.IsNullOrWhiteSpace(search) ? null : search.Trim(), Math.Max(1, page), 20, cancellationToken);

    public async Task<ContactMessageDto> OpenAsync(int id, CancellationToken cancellationToken = default)
    {
        var message = await messages.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("liên hệ", id);
        if (message.Status == ContactMessageStatus.New)
        {
            message.Status = ContactMessageStatus.Read;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Map(message);
    }

    public async Task UpdateAsync(int id, ContactMessageStatus status, string? adminNote, CancellationToken cancellationToken = default)
    {
        var message = await messages.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("liên hệ", id);
        if (!Enum.IsDefined(status))
        {
            throw new AppValidationException("Trạng thái không hợp lệ.");
        }

        message.Status = status;
        message.AdminNote = string.IsNullOrWhiteSpace(adminNote) ? null : adminNote.Trim()[..Math.Min(adminNote.Trim().Length, 1000)];
        if (status == ContactMessageStatus.Replied)
        {
            message.RepliedAt ??= timeProvider.GetUtcNow().UtcDateTime;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountNewAsync(CancellationToken cancellationToken = default) => messages.CountNewAsync(cancellationToken);

    private static ContactMessageDto Map(ContactMessage m) =>
        new(m.Id, m.FullName, m.Phone, m.Email, m.Subject, m.Message, m.Status, m.AdminNote, m.IpAddress, m.CreatedAt, m.RepliedAt);
}

// ================================================================== Store information

public interface IStoreInfoService
{
    Task<StoreInfoDto> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the store information; <paramref name="logo"/> replaces the logo picture (resized, the previous one is deleted).</summary>
    Task UpdateAsync(StoreInfoCommand command, (Stream Content, string FileName)? logo = null, CancellationToken cancellationToken = default);
}

public enum SocialNetwork
{
    Facebook,
    TikTok,
    Zalo
}

/// <summary>
/// Turns what admins paste for a social page into a full https link: "facebook.com/nhamoc" → "https://facebook.com/nhamoc",
/// "@nhamoc" (TikTok) → "https://www.tiktok.com/@nhamoc", a phone number (Zalo) → "https://zalo.me/0900000000".
/// A value with a scheme ("http://", "javascript:"...) is left as typed, for the validator to refuse.
/// </summary>
public static partial class SocialLinks
{
    public static string? Normalize(string? value, SocialNetwork network)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var link = value.Trim();
        if (SchemeRegex().IsMatch(link))
        {
            return link;
        }

        if (network == SocialNetwork.TikTok && TikTokHandleRegex().IsMatch(link))
        {
            return "https://www.tiktok.com/" + link;
        }

        if (network == SocialNetwork.Zalo && PhoneRegex().IsMatch(link))
        {
            return "https://zalo.me/" + new string(link.Where(char.IsAsciiDigit).ToArray());
        }

        return DomainRegex().IsMatch(link) ? "https://" + link : link;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial System.Text.RegularExpressions.Regex SchemeRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"^@[A-Za-z0-9._]{2,24}$")]
    private static partial System.Text.RegularExpressions.Regex TikTokHandleRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"^\+?[0-9][0-9 .\-]{7,16}$")]
    private static partial System.Text.RegularExpressions.Regex PhoneRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"^(www\.)?[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)+(/\S*)?$")]
    private static partial System.Text.RegularExpressions.Regex DomainRegex();
}

public sealed class StoreInfoCommandValidator : AbstractValidator<StoreInfoCommand>
{
    public StoreInfoCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên cửa hàng.").MaximumLength(150);
        RuleFor(x => x.LogoSubtitle).MaximumLength(40).WithMessage("Dòng chữ nhỏ dưới logo tối đa 40 ký tự.");
        RuleFor(x => x.Tagline).MaximumLength(200);
        RuleFor(x => x.About).MaximumLength(4000);
        RuleFor(x => x.Address).NotEmpty().WithMessage("Vui lòng nhập địa chỉ.").MaximumLength(300);
        RuleFor(x => x.WorkshopAddress).MaximumLength(300);
        RuleFor(x => x.Hotline).NotEmpty().WithMessage("Vui lòng nhập hotline.").MaximumLength(30).Matches(@"^[0-9+\s.\-()]+$").WithMessage("Hotline chỉ gồm chữ số.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Email không hợp lệ.").MaximumLength(256);
        RuleFor(x => x.OpeningHours).MaximumLength(150);
        RuleFor(x => x.FacebookUrl).Must(BeHttpsUrl).WithMessage("Link Facebook không hợp lệ - dán link trang, vd. https://facebook.com/tenshop.").MaximumLength(300);
        RuleFor(x => x.TikTokUrl).Must(BeHttpsUrl).WithMessage("Link TikTok không hợp lệ - dán link kênh (https://www.tiktok.com/@tenshop) hoặc gõ @tenshop.").MaximumLength(300);
        RuleFor(x => x.ZaloUrl).Must(BeHttpsUrl).WithMessage("Link Zalo không hợp lệ - dán link https://zalo.me/... hoặc gõ số điện thoại Zalo.").MaximumLength(300);
        // Rendered inside an <iframe>: only Google Maps embeds are allowed.
        RuleFor(x => x.GoogleMapsEmbedUrl)
            .Must(url => string.IsNullOrWhiteSpace(url)
                         || url.StartsWith("https://www.google.com/maps", StringComparison.OrdinalIgnoreCase)
                         || url.StartsWith("https://maps.google.com", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Chỉ chấp nhận link nhúng Google Maps (https://www.google.com/maps...).")
            .MaximumLength(1000);
    }

    private static bool BeHttpsUrl(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);
}

public sealed class StoreInfoService(
    IRepository<StoreInfo> store,
    IValidator<StoreInfoCommand> validator,
    IFileStorageService fileStorage,
    IUnitOfWork unitOfWork,
    IMemoryCache cache,
    IAuditLogService auditLog,
    IOptions<ApplicationSettings> siteOptions,
    ILogger<StoreInfoService> logger) : IStoreInfoService
{
    private const string CacheKey = "store-info";

    public async Task<StoreInfoDto> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out StoreInfoDto? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var entity = (await store.ListAsync(cancellationToken: cancellationToken)).OrderBy(s => s.Id).FirstOrDefault();
            var dto = entity is null ? FromSettings() : Map(entity);
            cache.Set(CacheKey, dto, TimeSpan.FromMinutes(10));
            return dto;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Rendered in the header/footer of every page (including error pages): never let it break the page.
            logger.LogWarning(ex, "Could not load store information from the database; using configuration values");
            var fallback = FromSettings();
            cache.Set(CacheKey, fallback, TimeSpan.FromMinutes(1));
            return fallback;
        }
    }

    public async Task UpdateAsync(StoreInfoCommand command, (Stream Content, string FileName)? logo = null, CancellationToken cancellationToken = default)
    {
        // Normalized first, so the form shows the full links if something else is refused.
        command.FacebookUrl = SocialLinks.Normalize(command.FacebookUrl, SocialNetwork.Facebook);
        command.TikTokUrl = SocialLinks.Normalize(command.TikTokUrl, SocialNetwork.TikTok);
        command.ZaloUrl = SocialLinks.Normalize(command.ZaloUrl, SocialNetwork.Zalo);
        await validator.EnsureValidAsync(command, cancellationToken);

        var entity = (await store.ListAsync(cancellationToken: cancellationToken)).OrderBy(s => s.Id).FirstOrDefault();
        var isNew = entity is null;
        entity ??= new StoreInfo();
        var previousLogo = entity.LogoUrl;

        // Validated and resized before anything changes: a refused picture leaves the store information as it was.
        var stored = logo is { } upload
            ? await fileStorage.SaveImageAsync(upload.Content, upload.FileName, ImagePreset.Logo, cancellationToken)
            : null;

        entity.Name = command.Name.Trim();
        entity.LogoSubtitle = Clean(command.LogoSubtitle);
        entity.Tagline = Clean(command.Tagline);
        entity.About = Clean(command.About);
        entity.Address = command.Address.Trim();
        entity.WorkshopAddress = Clean(command.WorkshopAddress);
        entity.Hotline = command.Hotline.Trim();
        entity.Email = command.Email.Trim();
        entity.OpeningHours = Clean(command.OpeningHours);
        entity.FacebookUrl = command.FacebookUrl;
        entity.TikTokUrl = command.TikTokUrl;
        entity.ZaloUrl = command.ZaloUrl;
        entity.GoogleMapsEmbedUrl = Clean(command.GoogleMapsEmbedUrl);

        if (stored is not null)
        {
            (entity.LogoUrl, entity.LogoWidth, entity.LogoHeight) = (stored.Url, stored.Width, stored.Height);
        }
        else if (command.RemoveLogo)
        {
            (entity.LogoUrl, entity.LogoWidth, entity.LogoHeight) = (null, null, null);
        }

        // The house icon never contains the name.
        entity.LogoShowsName = entity.LogoUrl is not null && command.LogoShowsName;

        if (isNew)
        {
            await store.AddAsync(entity, cancellationToken);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (stored is not null)
            {
                await fileStorage.DeleteAsync(stored.Url, CancellationToken.None);
            }

            throw;
        }

        if (previousLogo is not null && previousLogo != entity.LogoUrl)
        {
            await fileStorage.DeleteAsync(previousLogo, cancellationToken);
        }

        cache.Remove(CacheKey);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(StoreInfo), entity.Id.ToString(),
            stored is null ? "Cập nhật thông tin cửa hàng" : "Cập nhật thông tin cửa hàng (logo mới)", NewValues: command), cancellationToken);
    }

    private StoreInfoDto FromSettings()
    {
        var s = siteOptions.Value.Store;
        return new StoreInfoDto(s.Name, s.LogoSubtitle, siteOptions.Value.Tagline, null, s.Address, s.WorkshopAddress, s.Hotline, s.Email, s.OpeningHours,
            s.FacebookUrl, s.TikTokUrl, s.ZaloUrl, s.GoogleMapsEmbedUrl);
    }

    private static StoreInfoDto Map(StoreInfo s) =>
        new(s.Name, s.LogoSubtitle, s.Tagline, s.About, s.Address, s.WorkshopAddress, s.Hotline, s.Email, s.OpeningHours, s.FacebookUrl, s.TikTokUrl, s.ZaloUrl, s.GoogleMapsEmbedUrl)
        {
            LogoUrl = s.LogoUrl,
            LogoWidth = s.LogoWidth,
            LogoHeight = s.LogoHeight,
            LogoShowsName = s.LogoShowsName
        };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
