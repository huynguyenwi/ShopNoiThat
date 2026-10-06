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
    bool IsAvailable,
    bool IsSelected = true)
{
    public decimal LineTotal => UnitPrice * Quantity;

    /// <summary>Can be ticked for an order: still sold and in stock.</summary>
    public bool IsPurchasable => IsAvailable && Stock > 0;
    public int MaxQuantity => Math.Max(0, Math.Min(Stock, Domain.Entities.Cart.MaxQuantityPerItem));
    public bool ExceedsStock => Quantity > Stock;
    public string Url => $"/products/{ProductSlug}?variant={VariantId}";
}

/// <summary>
/// The cart. Subtotal, discount, total and warnings cover the <b>ticked</b> lines only: those are what the next order
/// contains; unticked lines stay in the cart.
/// </summary>
public sealed record CartDto(
    IReadOnlyList<CartLineDto> Items,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    string? CouponCode,
    string? CouponMessage,
    IReadOnlyList<string> Warnings)
{
    public int TotalQuantity => Items.Sum(i => i.Quantity);
    public bool IsEmpty => Items.Count == 0;

    public IReadOnlyList<CartLineDto> SelectedItems => Items.Where(i => i.IsSelected).ToList();
    public int SelectedQuantity => Items.Where(i => i.IsSelected).Sum(i => i.Quantity);
    public bool HasSelection => Items.Any(i => i.IsSelected);

    /// <summary>Every line that can be ordered is ticked (state of the "select all" box).</summary>
    public bool AllSelected => Items.Any(i => i.IsPurchasable) && Items.Where(i => i.IsPurchasable).All(i => i.IsSelected);

    public bool CanCheckout => HasSelection && SelectedItems.All(i => i.IsAvailable && !i.ExceedsStock);

    public static readonly CartDto Empty = new([], 0, 0, 0, null, null, []);
}

public sealed record PaymentMethodOption(PaymentMethod Method, string Name, string Description);

public sealed class CheckoutCommand
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
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
    PaymentInstructionsDto? PaymentInstructions,
    bool CanChangeShippingFee)
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
    public string Province { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
