using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Admin;

// ------------------------------------------------------------------ Dashboard

public sealed record ChartPointDto(string Label, decimal Value);

public sealed record StatusCountDto(OrderStatus Status, int Count);

public sealed record TopProductDto(int? ProductId, string Name, int Quantity, decimal Revenue);

public sealed record LowStockItemDto(int ProductId, int VariantId, string ProductName, string VariantName, string Sku, int Stock, int Threshold);

public sealed record DashboardDto(
    decimal TotalRevenue,
    decimal TodayRevenue,
    decimal MonthRevenue,
    int TotalOrders,
    int TodayOrders,
    int PendingOrders,
    int TotalCustomers,
    int NewCustomersThisMonth,
    int TotalProducts,
    int ActiveProducts,
    int LowStockCount,
    IReadOnlyList<LowStockItemDto> LowStock,
    IReadOnlyList<ChartPointDto> RevenueByDay,
    IReadOnlyList<ChartPointDto> OrdersByDay,
    IReadOnlyList<ChartPointDto> RevenueByMonth,
    IReadOnlyList<StatusCountDto> OrdersByStatus,
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyList<AdminOrderListItemDto> RecentOrders);

/// <summary>Minimal order data used to build revenue charts in Vietnam time.</summary>
public sealed record OrderFact(DateTime PlacedAt, decimal TotalAmount, OrderStatus Status);

// ------------------------------------------------------------------ Orders

public sealed class AdminOrderQuery
{
    public string? Search { get; set; }
    public OrderStatus? Status { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record AdminOrderListItemDto(
    int Id,
    string OrderCode,
    DateTime PlacedAt,
    string CustomerName,
    string CustomerPhone,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    PaymentMethod PaymentMethod,
    decimal TotalAmount,
    int ItemCount);

// ------------------------------------------------------------------ Users

public enum UserStatusFilter
{
    All = 0,
    Active = 1,
    Locked = 2
}

public sealed class AdminUserQuery
{
    public string? Search { get; set; }
    public string? Role { get; set; }
    public UserStatusFilter Status { get; set; } = UserStatusFilter.All;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record AdminUserListItemDto(
    string Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    IReadOnlyList<string> Roles,
    bool IsActive,
    bool IsLockedOut,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    int OrderCount,
    decimal TotalSpent);

public sealed record AdminUserDetailDto(
    AdminUserListItemDto User,
    string? Gender,
    DateOnly? DateOfBirth,
    string? AvatarUrl,
    int AccessFailedCount,
    IReadOnlyList<CustomerAddressDto> Addresses,
    IReadOnlyList<AdminOrderListItemDto> RecentOrders);

// ------------------------------------------------------------------ Audit / notifications

public sealed class AuditLogQuery
{
    public string? Search { get; set; }
    public AuditAction? Action { get; set; }
    public string? EntityName { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}

public sealed record AuditLogDto(
    int Id,
    DateTime CreatedAt,
    string? UserName,
    AuditAction Action,
    string EntityName,
    string? EntityId,
    string? Description,
    string? OldValues,
    string? NewValues,
    string? IpAddress);

public sealed record NotificationDto(int Id, NotificationType Type, string Title, string Message, string? Link, bool IsRead, DateTime CreatedAt);

// ------------------------------------------------------------------ Repositories

public interface IAdminReportRepository
{
    Task<IReadOnlyList<OrderFact>> GetOrderFactsAsync(DateTime fromUtc, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalRevenueAsync(IReadOnlyCollection<OrderStatus> revenueStatuses, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StatusCountDto>> GetOrderStatusCountsAsync(CancellationToken cancellationToken = default);
    Task<(int Total, int NewSince)> CountCustomersAsync(DateTime newSinceUtc, CancellationToken cancellationToken = default);
    Task<(int Total, int Active)> CountProductsAsync(CancellationToken cancellationToken = default);
    Task<(int Count, IReadOnlyList<LowStockItemDto> Items)> GetLowStockAsync(int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TopProductDto>> GetTopProductsAsync(IReadOnlyCollection<OrderStatus> statuses, DateTime fromUtc, int take, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminOrderListItemDto>> SearchOrdersAsync(AdminOrderQuery query, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default);
    Task<PagedResult<AuditLogDto>> SearchAuditLogsAsync(AuditLogQuery query, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationDto>> GetRoleNotificationsAsync(string role, int take, CancellationToken cancellationToken = default);
    Task<int> CountUnreadRoleNotificationsAsync(string role, CancellationToken cancellationToken = default);
    Task MarkRoleNotificationsReadAsync(string role, DateTime readAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Marks one notification of the role as read; null when it does not exist or belongs to another audience.</summary>
    Task<NotificationDto?> ReadRoleNotificationAsync(string role, int id, DateTime readAtUtc, CancellationToken cancellationToken = default);
}

public interface IUserAdminService
{
    Task<PagedResult<AdminUserListItemDto>> ListAsync(AdminUserQuery query, CancellationToken cancellationToken = default);
    Task<AdminUserDetailDto> GetAsync(string userId, CancellationToken cancellationToken = default);
    Task LockAsync(string userId, string? reason, CancellationToken cancellationToken = default);
    Task UnlockAsync(string userId, CancellationToken cancellationToken = default);
    Task SetRoleAsync(string userId, string role, bool enabled, CancellationToken cancellationToken = default);
}
