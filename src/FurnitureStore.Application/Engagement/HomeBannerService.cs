using System.Linq.Expressions;
using FluentValidation;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Media;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Application.Engagement;

// ================================================================== Home page banner

public sealed record HomeBannerStat(string Value, string Label);

/// <summary>The banner as shown: empty optional texts are null (their element is hidden).</summary>
/// <param name="PrimaryButtonUrl">Null: the main product line (the view knows its URL).</param>
/// <param name="ImageUrl">The uploaded picture, or the built-in illustration.</param>
/// <param name="IsCustomized">An admin saved a banner (false: the built-in one).</param>
public sealed record HomeBannerDto(
    string? Eyebrow,
    string Title,
    string? TitleHighlight,
    string? Description,
    string? PrimaryButtonText,
    string? PrimaryButtonUrl,
    string? SecondaryButtonText,
    string? SecondaryButtonUrl,
    IReadOnlyList<HomeBannerStat> Stats,
    string ImageUrl,
    int? ImageWidth,
    int? ImageHeight,
    string ImageAlt,
    bool HasCustomImage,
    bool IsCustomized);

/// <summary>Admin form of the banner (/admin/banner). The picture is uploaded next to it.</summary>
public sealed class HomeBannerCommand
{
    public string? Eyebrow { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TitleHighlight { get; set; }
    public string? Description { get; set; }
    public string? PrimaryButtonText { get; set; }
    public string? PrimaryButtonUrl { get; set; }
    public string? SecondaryButtonText { get; set; }
    public string? SecondaryButtonUrl { get; set; }
    public string? Stat1Value { get; set; }
    public string? Stat1Label { get; set; }
    public string? Stat2Value { get; set; }
    public string? Stat2Label { get; set; }
    public string? Stat3Value { get; set; }
    public string? Stat3Label { get; set; }
    public string? ImageAlt { get; set; }

    /// <summary>Go back to the built-in illustration (ignored when a new picture is uploaded).</summary>
    public bool RemoveImage { get; set; }

    public static HomeBannerCommand From(HomeBannerDto banner)
    {
        HomeBannerStat? Stat(int i) => i < banner.Stats.Count ? banner.Stats[i] : null;
        return new HomeBannerCommand
        {
            Eyebrow = banner.Eyebrow, Title = banner.Title, TitleHighlight = banner.TitleHighlight, Description = banner.Description,
            PrimaryButtonText = banner.PrimaryButtonText, PrimaryButtonUrl = banner.PrimaryButtonUrl,
            SecondaryButtonText = banner.SecondaryButtonText, SecondaryButtonUrl = banner.SecondaryButtonUrl,
            Stat1Value = Stat(0)?.Value, Stat1Label = Stat(0)?.Label,
            Stat2Value = Stat(1)?.Value, Stat2Label = Stat(1)?.Label,
            Stat3Value = Stat(2)?.Value, Stat3Label = Stat(2)?.Label,
            ImageAlt = banner.HasCustomImage ? banner.ImageAlt : null
        };
    }
}

/// <summary>The banner shown until an admin saves one (also after "Khôi phục mặc định").</summary>
public static class HomeBannerDefaults
{
    public const string ImageUrl = "/images/hero-dining-room.svg";
    public const int ImageWidth = 580;
    public const int ImageHeight = 471;
    public const string ImageAlt = "Minh họa phòng ăn với bộ bàn ghế gỗ sồi màu óc chó và đèn thả";

    public static readonly HomeBannerDto Banner = new(
        "Bàn ghế ăn đóng tại xưởng",
        "Bộ bàn ăn gỗ sồi Nga -",
        "màu óc chó",
        "Bàn 1m2 + 4 ghế hoặc bàn 1m6 + 6 ghế, làm từ gỗ sồi Nga nguyên khối, sơn màu óc chó ấm áp. "
        + "Chọn mẫu và gửi yêu cầu - cửa hàng gọi lại tư vấn, báo phí giao hàng & lắp đặt.",
        "Xem bộ bàn ăn",
        null,
        "Nhận tư vấn",
        "/contact",
        [new("24", "tháng bảo hành"), new("100%", "sản xuất tại xưởng"), new("7 ngày", "đổi trả nếu lỗi")],
        ImageUrl, ImageWidth, ImageHeight, ImageAlt,
        HasCustomImage: false,
        IsCustomized: false);
}

public interface IHomeBannerService
{
    Task<HomeBannerDto> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the banner; <paramref name="image"/> replaces the picture (resized, the previous upload is deleted).</summary>
    Task UpdateAsync(HomeBannerCommand command, (Stream Content, string FileName)? image, CancellationToken cancellationToken = default);

    /// <summary>Back to the built-in banner (the uploaded picture is deleted).</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public sealed class HomeBannerCommandValidator : AbstractValidator<HomeBannerCommand>
{
    public HomeBannerCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Vui lòng nhập tiêu đề banner.")
            .MaximumLength(120).WithMessage("Tiêu đề tối đa 120 ký tự.");
        RuleFor(x => x.Eyebrow).MaximumLength(60).WithMessage("Dòng chữ nhỏ phía trên tối đa 60 ký tự.");
        RuleFor(x => x.TitleHighlight).MaximumLength(80).WithMessage("Phần tiêu đề in nghiêng tối đa 80 ký tự.");
        RuleFor(x => x.Description).MaximumLength(400).WithMessage("Mô tả tối đa 400 ký tự.");

        RuleFor(x => x.PrimaryButtonText).MaximumLength(40).WithMessage("Chữ trên nút tối đa 40 ký tự.");
        RuleFor(x => x.PrimaryButtonUrl).MaximumLength(300).WithMessage("Đường dẫn tối đa 300 ký tự.")
            .Must(BeSafeLink).WithMessage(LinkMessage);
        RuleFor(x => x.SecondaryButtonText).MaximumLength(40).WithMessage("Chữ trên nút tối đa 40 ký tự.");
        RuleFor(x => x.SecondaryButtonUrl).MaximumLength(300).WithMessage("Đường dẫn tối đa 300 ký tự.")
            .Must(BeSafeLink).WithMessage(LinkMessage);
        RuleFor(x => x.SecondaryButtonUrl).NotEmpty().When(x => !string.IsNullOrWhiteSpace(x.SecondaryButtonText))
            .WithMessage("Vui lòng nhập đường dẫn cho nút phụ (hoặc xóa chữ trên nút để ẩn nút).");

        Stat(x => x.Stat1Value, x => x.Stat1Label, 1);
        Stat(x => x.Stat2Value, x => x.Stat2Label, 2);
        Stat(x => x.Stat3Value, x => x.Stat3Label, 3);

        RuleFor(x => x.ImageAlt).MaximumLength(200).WithMessage("Mô tả ảnh tối đa 200 ký tự.");
    }

    private const string LinkMessage = "Đường dẫn phải là trang trong website (bắt đầu bằng /), link https://... hoặc tel:số điện thoại.";

    /// <summary>A statistic needs both its number and its text, or neither (then it is hidden).</summary>
    private void Stat(Expression<Func<HomeBannerCommand, string?>> value, Expression<Func<HomeBannerCommand, string?>> label, int number)
    {
        var getValue = value.Compile();
        var getLabel = label.Compile();
        RuleFor(value).MaximumLength(20).WithMessage($"Số liệu {number}: con số tối đa 20 ký tự.");
        RuleFor(label).MaximumLength(40).WithMessage($"Số liệu {number}: mô tả tối đa 40 ký tự.");
        RuleFor(value).NotEmpty().When(x => !string.IsNullOrWhiteSpace(getLabel(x))).WithMessage($"Số liệu {number}: nhập cả con số (vd. 24).");
        RuleFor(label).NotEmpty().When(x => !string.IsNullOrWhiteSpace(getValue(x))).WithMessage($"Số liệu {number}: nhập cả mô tả (vd. tháng bảo hành).");
    }

    /// <summary>A page of this site ("/products"), an https:// link or a phone number - never javascript: or a protocol-relative URL.</summary>
    internal static bool BeSafeLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var link = value.Trim();
        if (link.Any(char.IsWhiteSpace) || link.Any(char.IsControl))
        {
            return false;
        }

        if (link.StartsWith('/'))
        {
            return link.Length == 1 || (link[1] != '/' && link[1] != '\\');
        }

        if (link.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return link.Length > 4 && link[4..].All(c => char.IsAsciiDigit(c) || c is '+' or '.' or '-');
        }

        return Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }
}

public sealed class HomeBannerService(
    IRepository<HomeBanner> banners,
    IValidator<HomeBannerCommand> validator,
    IFileStorageService fileStorage,
    IUnitOfWork unitOfWork,
    IMemoryCache cache,
    IAuditLogService auditLog,
    ILogger<HomeBannerService> logger) : IHomeBannerService
{
    private const string CacheKey = "home-banner";

    public async Task<HomeBannerDto> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out HomeBannerDto? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var entity = await FindAsync(cancellationToken);
            var dto = entity is null ? HomeBannerDefaults.Banner : Map(entity);
            cache.Set(CacheKey, dto, TimeSpan.FromMinutes(10));
            return dto;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The first thing on the home page: never let it break the page.
            logger.LogWarning(ex, "Could not load the home banner from the database; using the built-in banner");
            cache.Set(CacheKey, HomeBannerDefaults.Banner, TimeSpan.FromMinutes(1));
            return HomeBannerDefaults.Banner;
        }
    }

    public async Task UpdateAsync(HomeBannerCommand command, (Stream Content, string FileName)? image, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(command, cancellationToken);

        var entity = await FindAsync(cancellationToken);
        var isNew = entity is null;
        entity ??= new HomeBanner();
        var previousImage = entity.ImageUrl;

        // Validated and resized before anything changes: a refused picture leaves the banner as it was.
        var stored = image is { } upload
            ? await fileStorage.SaveImageAsync(upload.Content, upload.FileName, ImagePreset.Banner, cancellationToken)
            : null;

        entity.Eyebrow = Clean(command.Eyebrow);
        entity.Title = command.Title.Trim();
        entity.TitleHighlight = Clean(command.TitleHighlight);
        entity.Description = Clean(command.Description?.Replace("\r\n", "\n"));
        entity.PrimaryButtonText = Clean(command.PrimaryButtonText);
        entity.PrimaryButtonUrl = Clean(command.PrimaryButtonUrl);
        entity.SecondaryButtonText = Clean(command.SecondaryButtonText);
        entity.SecondaryButtonUrl = entity.SecondaryButtonText is null ? null : Clean(command.SecondaryButtonUrl);
        entity.Stat1Value = Clean(command.Stat1Value);
        entity.Stat1Label = Clean(command.Stat1Label);
        entity.Stat2Value = Clean(command.Stat2Value);
        entity.Stat2Label = Clean(command.Stat2Label);
        entity.Stat3Value = Clean(command.Stat3Value);
        entity.Stat3Label = Clean(command.Stat3Label);

        if (stored is not null)
        {
            (entity.ImageUrl, entity.ImageWidth, entity.ImageHeight) = (stored.Url, stored.Width, stored.Height);
        }
        else if (command.RemoveImage)
        {
            (entity.ImageUrl, entity.ImageWidth, entity.ImageHeight) = (null, null, null);
        }

        // The description of the built-in illustration is fixed; this one describes an uploaded picture.
        entity.ImageAlt = entity.ImageUrl is null ? null : Clean(command.ImageAlt);

        if (isNew)
        {
            await banners.AddAsync(entity, cancellationToken);
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

        if (previousImage is not null && previousImage != entity.ImageUrl)
        {
            await fileStorage.DeleteAsync(previousImage, cancellationToken);
        }

        cache.Remove(CacheKey);
        await auditLog.LogAsync(new AuditEntry(isNew ? AuditAction.Create : AuditAction.Update, nameof(HomeBanner), entity.Id.ToString(),
            stored is null ? "Cập nhật banner trang chủ" : "Cập nhật banner trang chủ (ảnh mới)", NewValues: command), cancellationToken);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(cancellationToken);
        if (entity is null)
        {
            return;
        }

        banners.Remove(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await fileStorage.DeleteAsync(entity.ImageUrl, cancellationToken);
        cache.Remove(CacheKey);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Delete, nameof(HomeBanner), entity.Id.ToString(), "Khôi phục banner trang chủ mặc định"),
            cancellationToken);
    }

    private async Task<HomeBanner?> FindAsync(CancellationToken cancellationToken) =>
        (await banners.ListAsync(cancellationToken: cancellationToken)).OrderBy(b => b.Id).FirstOrDefault();

    private static HomeBannerDto Map(HomeBanner b)
    {
        var stats = new[] { (b.Stat1Value, b.Stat1Label), (b.Stat2Value, b.Stat2Label), (b.Stat3Value, b.Stat3Label) }
            .Where(s => s.Item1 is not null && s.Item2 is not null)
            .Select(s => new HomeBannerStat(s.Item1!, s.Item2!))
            .ToList();
        var custom = b.ImageUrl is not null;
        return new HomeBannerDto(
            b.Eyebrow, b.Title, b.TitleHighlight, b.Description,
            b.PrimaryButtonText, b.PrimaryButtonUrl, b.SecondaryButtonText, b.SecondaryButtonUrl,
            stats,
            custom ? b.ImageUrl! : HomeBannerDefaults.ImageUrl,
            custom ? b.ImageWidth : HomeBannerDefaults.ImageWidth,
            custom ? b.ImageHeight : HomeBannerDefaults.ImageHeight,
            custom ? b.ImageAlt ?? $"{b.Title} {b.TitleHighlight}".Trim() : HomeBannerDefaults.ImageAlt,
            HasCustomImage: custom,
            IsCustomized: true);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
