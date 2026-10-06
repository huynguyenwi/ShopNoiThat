using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Sales;

/// <summary>
/// Applies an order status change together with its side effects, for both customers (cancel) and admins:
/// stock is returned and the coupon use released when an order is cancelled / refunded,
/// COD payments are marked paid on delivery, pending payments fail on cancellation.
/// Must run inside the caller's transaction.
/// </summary>
public sealed class OrderWorkflow(
    IInventoryRepository inventory,
    ICouponRepository coupons,
    IRepository<CouponUsage> couponUsages,
    TimeProvider timeProvider)
{
    public async Task ChangeStatusAsync(Order order, OrderStatus newStatus, string? changedBy, string? note, CancellationToken cancellationToken)
    {
        var previous = order.Status;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        order.ChangeStatus(newStatus, now, changedBy, note);

        if (OrderStatusTransitions.RestocksInventory(newStatus) && !OrderStatusTransitions.RestocksInventory(previous))
        {
            foreach (var item in order.Items)
            {
                if (item.ProductVariantId.HasValue)
                {
                    await inventory.ReleaseAsync(item.ProductVariantId.Value, item.Quantity, cancellationToken);
                }

                if (item.ProductId.HasValue)
                {
                    await inventory.AdjustProductAsync(item.ProductId.Value, stockDelta: item.Quantity, soldDelta: -item.Quantity, cancellationToken);
                }
            }

            if (order.CouponId.HasValue && newStatus == OrderStatus.Cancelled)
            {
                await coupons.ReleaseAsync(order.CouponId.Value, cancellationToken);
                foreach (var usage in await couponUsages.ListAsync(u => u.OrderId == order.Id, cancellationToken))
                {
                    couponUsages.Remove(usage);
                }
            }
        }

        foreach (var payment in order.Payments)
        {
            switch (newStatus)
            {
                case OrderStatus.Delivered when payment.Method == PaymentMethod.COD && payment.Status != PaymentStatus.Paid:
                    payment.Status = PaymentStatus.Paid;
                    payment.PaidAt = now;
                    payment.Note = "Thu tiền khi giao hàng";
                    break;
                case OrderStatus.Cancelled when payment.Status is PaymentStatus.Pending or PaymentStatus.Unpaid:
                    payment.Status = PaymentStatus.Failed;
                    payment.FailureReason = "Đơn hàng đã hủy";
                    break;
                case OrderStatus.Refunded when payment.Status == PaymentStatus.Paid:
                    payment.Status = PaymentStatus.Refunded;
                    break;
            }
        }

        if (newStatus == OrderStatus.Cancelled && order.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Unpaid)
        {
            order.PaymentStatus = PaymentStatus.Failed;
        }
    }
}
