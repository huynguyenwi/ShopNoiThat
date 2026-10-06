using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Sales;

public interface ICartRepository : IRepository<Cart>
{
    /// <summary>Tracked cart (with items) of the owner, or null.</summary>
    Task<Cart?> GetAsync(CartOwner owner, CancellationToken cancellationToken = default);

    /// <summary>Read-only lines with current product data (prices, stock, names, image).</summary>
    Task<IReadOnlyList<CartLineDto>> GetLinesAsync(int cartId, CancellationToken cancellationToken = default);

    Task<int> CountItemsAsync(CartOwner owner, CancellationToken cancellationToken = default);
}

public interface IOrderRepository : IRepository<Order>
{
    Task<bool> CodeExistsAsync(string orderCode, CancellationToken cancellationToken = default);

    /// <summary>Order with items, addresses, payments and history (tracked).</summary>
    Task<Order?> GetFullAsync(int id, CancellationToken cancellationToken = default);

    Task<Order?> GetFullByCodeAsync(string orderCode, CancellationToken cancellationToken = default);

    Task<PagedResult<OrderListItemDto>> ListForUserAsync(string userId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Delivered order line of this user for the product (proof of purchase for reviews).</summary>
    Task<OrderItem?> FindDeliveredItemAsync(string userId, int productId, CancellationToken cancellationToken = default);
}

public interface ICouponRepository : IRepository<Coupon>
{
    Task<Coupon?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<int> CountUsageByUserAsync(int couponId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Atomically increments UsedCount if the usage limit is not reached. Returns false when exhausted.</summary>
    Task<bool> TryConsumeAsync(int couponId, CancellationToken cancellationToken = default);

    Task ReleaseAsync(int couponId, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken = default);

    Task<PagedResult<CouponListItemDto>> SearchAsync(CouponQuery query, DateTime utcNow, CancellationToken cancellationToken = default);

    Task<CouponListItemDto?> GetListItemAsync(int id, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>Orders placed with the coupon (all statuses) and the revenue of those still valid (not cancelled / refunded).</summary>
    Task<(int OrderCount, decimal Revenue)> GetOrderStatsAsync(int couponId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CouponOrderDto>> GetRecentOrdersAsync(int couponId, int take, CancellationToken cancellationToken = default);

    /// <summary>Public coupons that can be used right now (active, within dates, not exhausted).</summary>
    Task<IReadOnlyList<Coupon>> ListOfferableAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, int>> CountUsagesByUserAsync(IReadOnlyCollection<int> couponIds, string userId, CancellationToken cancellationToken = default);

    /// <summary>Carts holding <paramref name="oldCode"/> get <paramref name="newCode"/> (null removes it).</summary>
    Task ReplaceCartCouponCodeAsync(string oldCode, string? newCode, CancellationToken cancellationToken = default);
}

/// <summary>Atomic stock operations (single UPDATE statements) used inside the order transaction.</summary>
public interface IInventoryRepository
{
    /// <summary>Decrements variant stock only if enough is available. Returns false otherwise.</summary>
    Task<bool> TryReserveAsync(int variantId, int quantity, CancellationToken cancellationToken = default);

    Task ReleaseAsync(int variantId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Keeps the product's denormalized stock and sold counters in sync with an order.</summary>
    Task AdjustProductAsync(int productId, int stockDelta, int soldDelta, CancellationToken cancellationToken = default);
}

public interface IWishlistRepository : IRepository<Wishlist>
{
    Task<Wishlist?> GetByUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetProductIdsAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed record PaymentInitResult(PaymentStatus Status, string? TransactionCode, string? Note, PaymentInstructionsDto? Instructions);

/// <summary>
/// One payment method (COD, bank transfer, later VNPay / MoMo). Card data never reaches this system:
/// gateways are redirect-based and only return a transaction reference.
/// </summary>
public interface IPaymentProvider
{
    Domain.Enums.PaymentMethod Method { get; }
    string DisplayName { get; }
    string Description { get; }
    Task<PaymentInitResult> InitiateAsync(Order order, CancellationToken cancellationToken = default);
}

public interface IPaymentService
{
    IReadOnlyList<PaymentMethodOption> GetAvailableMethods();
    bool IsEnabled(Domain.Enums.PaymentMethod method);
    Task<PaymentInitResult> InitiateAsync(Order order, CancellationToken cancellationToken = default);
    PaymentInstructionsDto? GetInstructions(Order order);
}

public interface INotificationService
{
    Task NotifyRoleAsync(string role, Domain.Enums.NotificationType type, string title, string message, string? link, CancellationToken cancellationToken = default);
    Task NotifyUserAsync(string userId, Domain.Enums.NotificationType type, string title, string message, string? link, CancellationToken cancellationToken = default);
}

public interface IShippingCalculator
{
    decimal Calculate(decimal subtotalAfterDiscount, string? province = null);
    decimal FreeShippingThreshold { get; }
}
