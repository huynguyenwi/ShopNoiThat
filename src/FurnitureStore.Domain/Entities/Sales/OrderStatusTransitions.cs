using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Domain.Entities;

/// <summary>
/// Allowed order status workflow:
/// Pending → Confirmed → Processing → Shipping → Delivered → Refunded,
/// and Cancelled from any state before Delivered.
/// </summary>
public static class OrderStatusTransitions
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Processing, OrderStatus.Cancelled],
        [OrderStatus.Processing] = [OrderStatus.Shipping, OrderStatus.Cancelled],
        [OrderStatus.Shipping] = [OrderStatus.Delivered, OrderStatus.Cancelled],
        [OrderStatus.Delivered] = [OrderStatus.Refunded],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Refunded] = []
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<OrderStatus> NextStatuses(OrderStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>Stock is returned to inventory when an order ends up cancelled or refunded.</summary>
    public static bool RestocksInventory(OrderStatus status) => status is OrderStatus.Cancelled or OrderStatus.Refunded;

    public static string DisplayName(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Chờ xác nhận",
        OrderStatus.Confirmed => "Đã xác nhận",
        OrderStatus.Processing => "Đang xử lý",
        OrderStatus.Shipping => "Đang giao hàng",
        OrderStatus.Delivered => "Đã giao hàng",
        OrderStatus.Cancelled => "Đã hủy",
        OrderStatus.Refunded => "Đã hoàn tiền",
        _ => status.ToString()
    };
}
