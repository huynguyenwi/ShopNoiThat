using FurnitureStore.Domain.Common;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Domain.Exceptions;

namespace FurnitureStore.Domain.Entities;

/// <summary>
/// Customer order. Customer contact, product names and prices are snapshotted so the order
/// stays correct even after the catalog or the user's profile changes.
/// </summary>
public class Order : AuditableEntity, IConcurrencyAware
{
    public string OrderCode { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.COD;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal TotalAmount { get; set; }

    public int? CouponId { get; set; }
    public Coupon? Coupon { get; set; }
    public string? CouponCode { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerNote { get; set; }
    public string? AdminNote { get; set; }
    public string? CancelReason { get; set; }

    public DateTime PlacedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public Guid Version { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderAddress> Addresses { get; set; } = new List<OrderAddress>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();

    /// <summary>A customer may cancel only before the workshop starts processing the order.</summary>
    public bool CanBeCancelledByCustomer => Status is OrderStatus.Pending or OrderStatus.Confirmed;

    public int TotalQuantity => Items.Sum(i => i.Quantity);

    /// <summary>Recalculates Subtotal and TotalAmount from items, discount and shipping fee.</summary>
    public void RecalculateTotals()
    {
        foreach (var item in Items)
        {
            item.LineTotal = item.UnitPrice * item.Quantity;
        }

        Subtotal = Items.Sum(i => i.LineTotal);
        DiscountAmount = Math.Clamp(DiscountAmount, 0, Subtotal);
        TotalAmount = Math.Max(0, Subtotal - DiscountAmount + ShippingFee);
    }

    /// <summary>
    /// Moves the order to <paramref name="newStatus"/> following <see cref="OrderStatusTransitions"/>,
    /// stamps the matching timestamp and appends a history row.
    /// </summary>
    public OrderStatusHistory ChangeStatus(OrderStatus newStatus, DateTime utcNow, string? changedBy, string? note = null)
    {
        if (!OrderStatusTransitions.CanTransition(Status, newStatus))
        {
            throw new DomainException(
                $"Không thể chuyển đơn hàng từ \"{OrderStatusTransitions.DisplayName(Status)}\" sang \"{OrderStatusTransitions.DisplayName(newStatus)}\".");
        }

        var history = new OrderStatusHistory
        {
            FromStatus = Status,
            ToStatus = newStatus,
            Note = note,
            ChangedBy = changedBy,
            ChangedAt = utcNow
        };

        Status = newStatus;
        switch (newStatus)
        {
            case OrderStatus.Confirmed:
                ConfirmedAt = utcNow;
                break;
            case OrderStatus.Shipping:
                ShippedAt = utcNow;
                break;
            case OrderStatus.Delivered:
                DeliveredAt = utcNow;
                if (PaymentMethod == PaymentMethod.COD)
                {
                    PaymentStatus = PaymentStatus.Paid;
                }
                break;
            case OrderStatus.Cancelled:
                CancelledAt = utcNow;
                CancelReason = note;
                break;
            case OrderStatus.Refunded:
                PaymentStatus = PaymentStatus.Refunded;
                break;
        }

        StatusHistory.Add(history);
        return history;
    }
}

public class OrderItem : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    // Snapshot at purchase time
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? ColorName { get; set; }
    public string? MaterialName { get; set; }
    public string? SizeName { get; set; }

    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class OrderAddress : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public AddressType AddressType { get; set; } = AddressType.Shipping;
    public string RecipientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string AddressLine { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;

    /// <summary>Optional: since 07/2025 Vietnam uses a two-level (province → ward) administration.</summary>
    public string? District { get; set; }

    public string Province { get; set; } = string.Empty;

    public string FullAddress => string.Join(", ",
        new[] { AddressLine, Ward, District, Province }.Where(part => !string.IsNullOrWhiteSpace(part)));
}

public class OrderStatusHistory : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string? Note { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>
/// Payment attempt / record for an order. Never stores card numbers, CVV or gateway secrets -
/// only the gateway transaction reference and result code.
/// </summary>
public class Payment : AuditableEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public string? Provider { get; set; }
    public string? TransactionCode { get; set; }
    public string? ProviderResponseCode { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? Note { get; set; }
}
