using System.Globalization;
using FluentValidation;
using FurnitureStore.Application.Common.Utilities;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Sales;

/// <summary>Admin create / edit form of a coupon. <see cref="StartsAt"/> / <see cref="EndsAt"/> are Vietnam local time.</summary>
public sealed class CouponCommand
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;

    /// <summary>Percent (1-100) or VND amount, depending on <see cref="DiscountType"/>.</summary>
    public decimal DiscountValue { get; set; }

    /// <summary>Cap of a percentage discount; ignored for fixed amounts.</summary>
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>Empty = any order.</summary>
    public decimal? MinOrderAmount { get; set; }

    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    /// <summary>Empty = unlimited.</summary>
    public int? UsageLimit { get; set; }

    /// <summary>Empty = unlimited.</summary>
    public int? UsageLimitPerUser { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsPublic { get; set; }
}

public sealed class CouponCommandValidator : AbstractValidator<CouponCommand>
{
    public CouponCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Vui lòng nhập mã giảm giá.")
            .Length(3, Coupon.CodeMaxLength).WithMessage($"Mã giảm giá dài từ 3 đến {Coupon.CodeMaxLength} ký tự.")
            .Matches("^[A-Z0-9_-]+$").WithMessage("Mã chỉ gồm chữ không dấu, số, gạch dưới hoặc gạch ngang (không có khoảng trắng).");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên chương trình.")
            .MaximumLength(150).WithMessage("Tên tối đa 150 ký tự.");
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Mô tả tối đa 500 ký tự.");
        RuleFor(x => x.DiscountType).IsInEnum().WithMessage("Kiểu giảm giá không hợp lệ.");

        RuleFor(x => x.DiscountValue).InclusiveBetween(1, 100).WithMessage("Phần trăm giảm phải từ 1 đến 100.")
            .When(x => x.DiscountType == DiscountType.Percentage);
        RuleFor(x => x.DiscountValue).GreaterThan(0).WithMessage("Số tiền giảm phải lớn hơn 0.")
            .LessThanOrEqualTo(1_000_000_000).WithMessage("Số tiền giảm quá lớn.")
            .When(x => x.DiscountType == DiscountType.FixedAmount);
        RuleFor(x => x.MaxDiscountAmount).GreaterThan(0).WithMessage("Mức giảm tối đa phải lớn hơn 0 (để trống nếu không giới hạn).")
            .When(x => x.DiscountType == DiscountType.Percentage && x.MaxDiscountAmount.HasValue);
        RuleFor(x => x.MinOrderAmount).GreaterThanOrEqualTo(0).WithMessage("Giá trị đơn tối thiểu không được âm.")
            .LessThanOrEqualTo(10_000_000_000).WithMessage("Giá trị đơn tối thiểu quá lớn.")
            .When(x => x.MinOrderAmount.HasValue);

        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt).WithMessage("Thời gian kết thúc phải sau thời gian bắt đầu.")
            .When(x => x.StartsAt.HasValue && x.EndsAt.HasValue);
        RuleFor(x => x.UsageLimit).GreaterThanOrEqualTo(1).WithMessage("Tổng số lượt phải từ 1 trở lên (để trống nếu không giới hạn).")
            .When(x => x.UsageLimit.HasValue);
        RuleFor(x => x.UsageLimitPerUser).GreaterThanOrEqualTo(1).WithMessage("Số lượt mỗi khách phải từ 1 trở lên (để trống nếu không giới hạn).")
            .When(x => x.UsageLimitPerUser.HasValue);
        RuleFor(x => x.UsageLimitPerUser).LessThanOrEqualTo(x => x.UsageLimit).WithMessage("Số lượt mỗi khách không được lớn hơn tổng số lượt.")
            .When(x => x.UsageLimit.HasValue && x.UsageLimitPerUser.HasValue);
    }
}

public sealed class CouponQuery
{
    public string? Search { get; set; }
    public CouponStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record CouponListItemDto(
    int Id,
    string Code,
    string Name,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal MinOrderAmount,
    DateTime? StartsAt,
    DateTime? EndsAt,
    int? UsageLimit,
    int? UsageLimitPerUser,
    int UsedCount,
    bool IsActive,
    bool IsPublic,
    CouponStatus Status,
    decimal TotalDiscount)
{
    public string Benefit => CouponText.Benefit(DiscountType, DiscountValue, MaxDiscountAmount);
}

/// <summary>An order placed with the coupon (cancelled ones included, their usage having been released).</summary>
public sealed record CouponOrderDto(
    int OrderId,
    string OrderCode,
    string CustomerName,
    string CustomerEmail,
    OrderStatus Status,
    decimal DiscountAmount,
    decimal TotalAmount,
    DateTime PlacedAt);

public sealed record CouponDetailDto(
    CouponListItemDto Coupon,
    string? Description,
    int OrderCount,
    decimal Revenue,
    IReadOnlyList<CouponOrderDto> RecentOrders,
    DateTime CreatedAt,
    string? CreatedBy)
{
    /// <summary>Orders keep their coupon; code and deletion are locked once one exists.</summary>
    public bool HasOrders => OrderCount > 0;
}

/// <summary>A public coupon shown in the cart / checkout, with whether the current cart qualifies.</summary>
public sealed record CouponOfferDto(
    string Code,
    string Name,
    string? Description,
    string Benefit,
    string Conditions,
    bool IsEligible,
    bool IsApplied,
    string? Reason,
    decimal EstimatedDiscount);

/// <summary>Vietnamese descriptions of coupon terms, shared by admin pages and the storefront.</summary>
public static class CouponText
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    public static string Money(decimal value) => value.ToString("#,0", Vietnamese) + "₫";

    public static string Benefit(DiscountType type, decimal value, decimal? maxDiscount) => type == DiscountType.Percentage
        ? $"Giảm {value.ToString("0.##", Vietnamese)}%" + (maxDiscount is decimal cap ? $", tối đa {Money(cap)}" : string.Empty)
        : $"Giảm {Money(value)}";

    public static string Conditions(decimal minOrderAmount, int? usageLimitPerUser, DateTime? endsAtUtc)
    {
        var parts = new List<string> { minOrderAmount > 0 ? $"Đơn từ {Money(minOrderAmount)}" : "Mọi đơn hàng" };
        if (usageLimitPerUser is int perUser)
        {
            parts.Add($"mỗi khách {perUser} lần");
        }

        if (endsAtUtc is DateTime ends)
        {
            parts.Add("HSD " + VietnamTime.ToLocal(ends).ToString("HH:mm dd/MM/yyyy", Vietnamese));
        }

        return string.Join(" · ", parts);
    }

    public static string StatusName(CouponStatus status) => status switch
    {
        CouponStatus.Running => "Đang chạy",
        CouponStatus.Scheduled => "Chưa bắt đầu",
        CouponStatus.Expired => "Hết hạn",
        CouponStatus.Exhausted => "Hết lượt",
        _ => "Đã tắt"
    };
}
