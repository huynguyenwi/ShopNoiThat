using FurnitureStore.Application.Catalog;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FurnitureStore.Web.Infrastructure;

/// <summary>Vietnamese labels for enums shown in the UI.</summary>
public static class Labels
{
    public static string Of(ProductStatus status) => status switch
    {
        ProductStatus.Draft => "Nháp",
        ProductStatus.Active => "Đang bán",
        ProductStatus.Inactive => "Tạm ẩn",
        ProductStatus.Discontinued => "Ngừng kinh doanh",
        _ => status.ToString()
    };

    public static string BadgeClass(ProductStatus status) => status switch
    {
        ProductStatus.Active => "text-bg-success",
        ProductStatus.Draft => "text-bg-secondary",
        ProductStatus.Inactive => "text-bg-warning",
        _ => "text-bg-dark"
    };

    public static string Of(MaterialGroup group) => group switch
    {
        MaterialGroup.NaturalWood => "Gỗ tự nhiên",
        MaterialGroup.EngineeredWood => "Gỗ công nghiệp",
        MaterialGroup.Fabric => "Vải",
        MaterialGroup.Leather => "Da",
        MaterialGroup.Metal => "Kim loại",
        MaterialGroup.Stone => "Đá",
        MaterialGroup.Glass => "Kính",
        MaterialGroup.Rattan => "Mây tre",
        _ => "Khác"
    };

    public static string Of(FurnitureType type) => FurnitureTypes.NameOf(type);

    public static string Of(OrderStatus status) => OrderStatusTransitions.DisplayName(status);

    public static string BadgeClass(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "text-bg-warning",
        OrderStatus.Confirmed => "text-bg-info",
        OrderStatus.Processing => "text-bg-primary",
        OrderStatus.Shipping => "text-bg-primary",
        OrderStatus.Delivered => "text-bg-success",
        OrderStatus.Cancelled => "text-bg-secondary",
        OrderStatus.Refunded => "text-bg-dark",
        _ => "text-bg-light"
    };

    public static string Icon(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "bi-hourglass-split",
        OrderStatus.Confirmed => "bi-check2-circle",
        OrderStatus.Processing => "bi-hammer",
        OrderStatus.Shipping => "bi-truck",
        OrderStatus.Delivered => "bi-house-check",
        OrderStatus.Cancelled => "bi-x-circle",
        OrderStatus.Refunded => "bi-arrow-counterclockwise",
        _ => "bi-circle"
    };

    public static string Of(PaymentStatus status) => status switch
    {
        PaymentStatus.Unpaid => "Chưa thanh toán",
        PaymentStatus.Pending => "Chờ thanh toán",
        PaymentStatus.Paid => "Đã thanh toán",
        PaymentStatus.Failed => "Không thành công",
        PaymentStatus.Refunded => "Đã hoàn tiền",
        _ => status.ToString()
    };

    public static string BadgeClass(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => "text-bg-success",
        PaymentStatus.Pending => "text-bg-warning",
        PaymentStatus.Refunded => "text-bg-dark",
        PaymentStatus.Failed => "text-bg-secondary",
        _ => "text-bg-light"
    };

    public static string Of(PaymentMethod method) => method switch
    {
        PaymentMethod.COD => "Thanh toán khi nhận hàng (COD)",
        PaymentMethod.BankTransfer => "Chuyển khoản ngân hàng",
        PaymentMethod.VNPay => "VNPay",
        PaymentMethod.MoMo => "Ví MoMo",
        _ => method.ToString()
    };

    public static IEnumerable<SelectListItem> Options<TEnum>(Func<TEnum, string> label, TEnum? selected = null) where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => new SelectListItem(label(value), value.ToString(), selected.HasValue && selected.Value.Equals(value)));
}

public static class AiLabels
{
    public static string Of(FurnitureStore.Domain.Enums.AIConversationType type) => type switch
    {
        FurnitureStore.Domain.Enums.AIConversationType.ProductAdvice => "Tư vấn sản phẩm",
        FurnitureStore.Domain.Enums.AIConversationType.Recommendation => "Gợi ý sản phẩm",
        FurnitureStore.Domain.Enums.AIConversationType.ColorAdvice => "Tư vấn màu",
        FurnitureStore.Domain.Enums.AIConversationType.StyleAdvice => "Chọn phong cách",
        FurnitureStore.Domain.Enums.AIConversationType.PriceQuote => "Báo giá",
        _ => "Hỏi đáp"
    };

    public static string Icon(FurnitureStore.Domain.Enums.AIConversationType type) => type switch
    {
        FurnitureStore.Domain.Enums.AIConversationType.ProductAdvice => "bi-box-seam",
        FurnitureStore.Domain.Enums.AIConversationType.Recommendation => "bi-lightbulb",
        FurnitureStore.Domain.Enums.AIConversationType.ColorAdvice => "bi-palette",
        FurnitureStore.Domain.Enums.AIConversationType.StyleAdvice => "bi-house-heart",
        FurnitureStore.Domain.Enums.AIConversationType.PriceQuote => "bi-calculator",
        _ => "bi-stars"
    };
}

public static class QuoteLabels
{
    public static string Badge(QuoteStatus status) => status switch
    {
        QuoteStatus.New => "text-bg-warning",
        QuoteStatus.Reviewing => "text-bg-info",
        QuoteStatus.Quoted => "text-bg-primary",
        QuoteStatus.Accepted => "text-bg-success",
        QuoteStatus.Rejected => "text-bg-secondary",
        _ => "text-bg-light border"
    };

    public static string Of(QuoteStatus status) => QuoteStatusTransitions.DisplayName(status);

    public static string Of(PriceRuleType type) => FurnitureStore.Application.Quotes.PriceCalculatorService.RuleTypeName(type);

    public static string Of(FinishType finish) => FurnitureStore.Application.Quotes.PriceCalculatorService.FinishName(finish);
}

/// <summary>Badge colors of coupon statuses (admin pages).</summary>
public static class CouponBadge
{
    public static string For(CouponStatus status) => status switch
    {
        CouponStatus.Running => "text-bg-success",
        CouponStatus.Scheduled => "text-bg-info",
        CouponStatus.Exhausted => "text-bg-warning",
        CouponStatus.Expired => "text-bg-secondary",
        _ => "text-bg-light border"
    };
}
