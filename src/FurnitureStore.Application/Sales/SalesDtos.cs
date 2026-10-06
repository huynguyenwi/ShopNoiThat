using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Sales;

/// <summary>Who owns a cart: a signed-in user or an anonymous visitor (cookie id).</summary>
public sealed record CartOwner(string? UserId, string? AnonymousId)
{
    public bool IsEmpty => string.IsNullOrEmpty(UserId) && string.IsNullOrEmpty(AnonymousId);
}

public sealed record CartLineDto(
    int ItemId,
    int VariantId,
    int ProductId,
    string ProductName,
    string ProductSlug,
    string VariantName,
    string Sku,
    string? ImageUrl,
    string? ColorName,
    string? MaterialName,
    string? SizeName,
    decimal UnitPrice,
    decimal? OriginalPrice,
    int Quantity,
    int Stock,
    bool IsAvailable)
{
    public decimal LineTotal => UnitPrice * Quantity;
    public int MaxQuantity => Math.Max(0, Math.Min(Stock, Domain.Entities.Cart.MaxQuantityPerItem));
    public bool ExceedsStock => Quantity > Stock;
    public string Url => $"/products/{ProductSlug}?variant={VariantId}";
}

public sealed record CartDto(
    IReadOnlyList<CartLineDto> Items,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingFee,
    decimal Total,
    string? CouponCode,
    string? CouponMessage,
    decimal FreeShippingThreshold,
    IReadOnlyList<string> Warnings)
{
    public int TotalQuantity => Items.Sum(i => i.Quantity);
    public bool IsEmpty => Items.Count == 0;
    public bool CanCheckout => Items.Count > 0 && Items.All(i => i.IsAvailable && !i.ExceedsStock);
    public decimal AmountToFreeShipping => Math.Max(0, FreeShippingThreshold - Subtotal);

    public static CartDto Empty(decimal freeShippingThreshold) => new([], 0, 0, 0, 0, null, null, freeShippingThreshold, []);
}

public sealed record PaymentMethodOption(PaymentMethod Method, string Name, string Description);

public sealed class CheckoutCommand
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string? District { get; set; }
    public string Province { get; set; } = string.Empty;
    public string? Note { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.COD;
    public bool SaveAddress { get; set; }
}

public sealed record OrderPlacedResult(int OrderId, string OrderCode, decimal Total, PaymentMethod PaymentMethod, PaymentInstructionsDto? PaymentInstructions);

public sealed record PaymentInstructionsDto(string BankName, string AccountNumber, string AccountName, string Branch, string TransferNote, decimal Amount);

public sealed record OrderListItemDto(
    int Id,
    string OrderCode,
    DateTime PlacedAt,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    PaymentMethod PaymentMethod,
    decimal TotalAmount,
    int ItemCount,
    string? FirstItemName,
    string? FirstItemImage);

public sealed record OrderItemDto(
    int Id,
    int? ProductId,
    string ProductName,
    string? ProductSlug,
    string? VariantName,
    string Sku,
    string? ImageUrl,
    string? ColorName,
    string? MaterialName,
    string? SizeName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record OrderAddressDto(string RecipientName, string Phone, string? Email, string AddressLine, string Ward, string? District, string Province)
{
    public string FullAddress => string.Join(", ", new[] { AddressLine, Ward, District, Province }.Where(p => !string.IsNullOrWhiteSpace(p)));
}

public sealed record OrderStatusHistoryDto(OrderStatus? FromStatus, OrderStatus ToStatus, string? Note, string? ChangedBy, DateTime ChangedAt);

public sealed record PaymentDto(int Id, PaymentMethod Method, PaymentStatus Status, decimal Amount, string? TransactionCode, DateTime CreatedAt, DateTime? PaidAt, string? Note);

public sealed record OrderDetailDto(
    int Id,
    string OrderCode,
    string UserId,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    PaymentMethod PaymentMethod,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingFee,
    decimal TotalAmount,
    string? CouponCode,
    string CustomerName,
    string CustomerPhone,
    string CustomerEmail,
    string? CustomerNote,
    string? AdminNote,
    string? CancelReason,
    DateTime PlacedAt,
    DateTime? ConfirmedAt,
    DateTime? ShippedAt,
    DateTime? DeliveredAt,
    DateTime? CancelledAt,
    Guid Version,
    OrderAddressDto? ShippingAddress,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderStatusHistoryDto> History,
    IReadOnlyList<PaymentDto> Payments,
    PaymentInstructionsDto? PaymentInstructions)
{
    public bool CanBeCancelledByCustomer => Status is OrderStatus.Pending or OrderStatus.Confirmed;
    public int TotalQuantity => Items.Sum(i => i.Quantity);
}

public sealed record CustomerAddressDto(
    int Id,
    string? Label,
    string RecipientName,
    string Phone,
    string AddressLine,
    string Ward,
    string? District,
    string Province,
    bool IsDefault)
{
    public string FullAddress => string.Join(", ", new[] { AddressLine, Ward, District, Province }.Where(p => !string.IsNullOrWhiteSpace(p)));
}

public sealed class CustomerAddressCommand
{
    public string? Label { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string? District { get; set; }
    public string Province { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
