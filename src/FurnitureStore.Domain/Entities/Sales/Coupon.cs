using System.Globalization;
using FurnitureStore.Domain.Common;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Domain.Entities;

public class Coupon : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public DiscountType DiscountType { get; set; }

    /// <summary>Percent (0-100) for <see cref="DiscountType.Percentage"/>, VND amount for FixedAmount.</summary>
    public decimal DiscountValue { get; set; }

    /// <summary>Cap for percentage coupons; null = no cap.</summary>
    public decimal? MaxDiscountAmount { get; set; }

    public decimal MinOrderAmount { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int? UsageLimit { get; set; }
    public int? UsageLimitPerUser { get; set; }
    public int UsedCount { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Listed to customers in the cart / checkout ("Ưu đãi dành cho bạn"). Private codes are only typed in.</summary>
    public bool IsPublic { get; set; }

    public Guid Version { get; set; }

    public ICollection<CouponUsage> Usages { get; set; } = new List<CouponUsage>();

    public const int CodeMaxLength = 30;

    /// <summary>Codes are stored and compared upper-case, without surrounding spaces.</summary>
    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public CouponStatus GetStatus(DateTime utcNow)
    {
        if (!IsActive) return CouponStatus.Inactive;
        if (EndsAt.HasValue && utcNow > EndsAt.Value) return CouponStatus.Expired;
        if (UsageLimit.HasValue && UsedCount >= UsageLimit.Value) return CouponStatus.Exhausted;
        if (StartsAt.HasValue && utcNow < StartsAt.Value) return CouponStatus.Scheduled;
        return CouponStatus.Running;
    }

    /// <summary>Returns null when the coupon can be applied, otherwise the reason (user-facing).</summary>
    public string? GetInvalidReason(decimal subtotal, DateTime utcNow, int usedByThisUser)
    {
        if (!IsActive) return "Mã giảm giá không còn hiệu lực.";
        if (StartsAt.HasValue && utcNow < StartsAt.Value) return "Mã giảm giá chưa đến thời gian áp dụng.";
        if (EndsAt.HasValue && utcNow > EndsAt.Value) return "Mã giảm giá đã hết hạn.";
        if (UsageLimit.HasValue && UsedCount >= UsageLimit.Value) return "Mã giảm giá đã hết lượt sử dụng.";
        if (UsageLimitPerUser.HasValue && usedByThisUser >= UsageLimitPerUser.Value) return "Bạn đã sử dụng hết lượt cho mã giảm giá này.";
        if (subtotal < MinOrderAmount) return $"Đơn hàng tối thiểu {Money(MinOrderAmount)} để áp dụng mã này.";
        return null;
    }

    /// <summary>Discount amount for the given subtotal, never above the subtotal itself.</summary>
    public decimal CalculateDiscount(decimal subtotal)
    {
        if (subtotal <= 0) return 0;

        var discount = DiscountType switch
        {
            DiscountType.Percentage => Math.Round(subtotal * Math.Clamp(DiscountValue, 0, 100) / 100m, 0, MidpointRounding.AwayFromZero),
            DiscountType.FixedAmount => Math.Max(0, DiscountValue),
            _ => 0
        };

        if (MaxDiscountAmount.HasValue)
        {
            discount = Math.Min(discount, MaxDiscountAmount.Value);
        }

        return Math.Min(discount, subtotal);
    }

    private static string Money(decimal value) => value.ToString("#,0", CultureInfo.GetCultureInfo("vi-VN")) + "₫";
}

public class CouponUsage : BaseEntity
{
    public int CouponId { get; set; }
    public Coupon Coupon { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public decimal DiscountAmount { get; set; }
    public DateTime UsedAt { get; set; }
}
