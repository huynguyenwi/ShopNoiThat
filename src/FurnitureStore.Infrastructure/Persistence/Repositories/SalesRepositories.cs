using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStore.Infrastructure.Persistence.Repositories;

public sealed class CartRepository(ApplicationDbContext context) : EfRepository<Cart>(context), ICartRepository
{
    public Task<Cart?> GetAsync(CartOwner owner, CancellationToken cancellationToken = default)
    {
        var carts = Context.Carts.Include(c => c.Items);
        return owner.UserId is not null
            ? carts.FirstOrDefaultAsync(c => c.UserId == owner.UserId, cancellationToken)
            : carts.FirstOrDefaultAsync(c => c.AnonymousId == owner.AnonymousId, cancellationToken);
    }

    public async Task<IReadOnlyList<CartLineDto>> GetLinesAsync(int cartId, CancellationToken cancellationToken = default)
    {
        var rows = await Context.CartItems.AsNoTracking()
            .Where(i => i.CartId == cartId)
            .OrderBy(i => i.AddedAt).ThenBy(i => i.Id)
            .Select(i => new
            {
                i.Id,
                i.ProductVariantId,
                i.Quantity,
                i.IsSelected,
                i.ProductVariant.ProductId,
                ProductName = i.ProductVariant.Product.Name,
                ProductSlug = i.ProductVariant.Product.Slug,
                ProductStatus = i.ProductVariant.Product.Status,
                VariantName = i.ProductVariant.Name,
                i.ProductVariant.Sku,
                i.ProductVariant.Price,
                i.ProductVariant.OriginalPrice,
                i.ProductVariant.StockQuantity,
                i.ProductVariant.IsActive,
                VariantImage = i.ProductVariant.Images.OrderBy(img => img.DisplayOrder).Select(img => img.Url).FirstOrDefault(),
                ProductImage = i.ProductVariant.Product.Images.Where(img => img.ProductVariantId == null)
                    .OrderByDescending(img => img.IsPrimary).ThenBy(img => img.DisplayOrder).Select(img => img.Url).FirstOrDefault(),
                Color = i.ProductVariant.Colors.Where(c => c.IsPrimary).Select(c => c.Color.Name).FirstOrDefault(),
                Material = i.ProductVariant.Materials.Where(m => m.IsPrimary).Select(m => m.Material.Name).FirstOrDefault(),
                Size = i.ProductVariant.Sizes.Where(s => s.IsPrimary).Select(s => s.Size.Name).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new CartLineDto(
            r.Id, r.ProductVariantId, r.ProductId, r.ProductName, r.ProductSlug, r.VariantName, r.Sku,
            r.VariantImage ?? r.ProductImage, r.Color, r.Material, r.Size,
            r.Price, r.OriginalPrice > r.Price ? r.OriginalPrice : null,
            r.Quantity, r.StockQuantity, r.IsActive && r.ProductStatus == ProductStatus.Active, r.IsSelected)).ToList();
    }

    public async Task<int> CountItemsAsync(CartOwner owner, CancellationToken cancellationToken = default)
    {
        var items = owner.UserId is not null
            ? Context.CartItems.Where(i => i.Cart.UserId == owner.UserId)
            : Context.CartItems.Where(i => i.Cart.AnonymousId == owner.AnonymousId);
        return await items.SumAsync(i => (int?)i.Quantity, cancellationToken) ?? 0;
    }
}

public sealed class OrderRepository(ApplicationDbContext context) : EfRepository<Order>(context), IOrderRepository
{
    public Task<bool> CodeExistsAsync(string orderCode, CancellationToken cancellationToken = default) =>
        Context.Orders.AnyAsync(o => o.OrderCode == orderCode, cancellationToken);

    public Task<Order?> GetFullAsync(int id, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Order?> GetFullByCodeAsync(string orderCode, CancellationToken cancellationToken = default)
    {
        // Order codes are generated upper-case; accept any casing typed by customers.
        var normalized = (orderCode ?? string.Empty).Trim().ToUpperInvariant();
        return FullQuery().FirstOrDefaultAsync(o => o.OrderCode == normalized, cancellationToken);
    }

    public async Task<PagedResult<OrderListItemDto>> ListForUserAsync(string userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Context.Orders.AsNoTracking().Where(o => o.UserId == userId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(o => o.PlacedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderListItemDto(
                o.Id, o.OrderCode, o.PlacedAt, o.Status, o.PaymentStatus, o.PaymentMethod, o.TotalAmount,
                o.Items.Sum(i => i.Quantity),
                o.Items.OrderBy(i => i.Id).Select(i => i.ProductName).FirstOrDefault(),
                o.Items.OrderBy(i => i.Id).Select(i => i.ImageUrl).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderListItemDto>(rows, total, page, pageSize);
    }

    public Task<OrderItem?> FindDeliveredItemAsync(string userId, int productId, CancellationToken cancellationToken = default) =>
        Context.OrderItems
            .Where(i => i.ProductId == productId && i.Order.UserId == userId && i.Order.Status == OrderStatus.Delivered)
            .OrderByDescending(i => i.Order.DeliveredAt)
            .FirstOrDefaultAsync(cancellationToken);

    private IQueryable<Order> FullQuery() =>
        Context.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.Addresses)
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .AsSplitQuery();
}

public sealed class CouponRepository(ApplicationDbContext context) : EfRepository<Coupon>(context), ICouponRepository
{
    public Task<Coupon?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Context.Coupons.FirstOrDefaultAsync(c => c.Code == normalized, cancellationToken);
    }

    public Task<int> CountUsageByUserAsync(int couponId, string userId, CancellationToken cancellationToken = default) =>
        Context.CouponUsages.CountAsync(u => u.CouponId == couponId && u.UserId == userId, cancellationToken);

    public async Task<bool> TryConsumeAsync(int couponId, CancellationToken cancellationToken = default)
    {
        var version = Guid.NewGuid();
        var updated = await Context.Coupons
            .Where(c => c.Id == couponId && c.IsActive && (c.UsageLimit == null || c.UsedCount < c.UsageLimit))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.UsedCount, c => c.UsedCount + 1)
                .SetProperty(c => c.Version, version), cancellationToken);
        return updated == 1;
    }

    public Task ReleaseAsync(int couponId, CancellationToken cancellationToken = default)
    {
        var version = Guid.NewGuid();
        return Context.Coupons
            .Where(c => c.Id == couponId && c.UsedCount > 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.UsedCount, c => c.UsedCount - 1)
                .SetProperty(c => c.Version, version), cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken = default)
    {
        var normalized = Coupon.NormalizeCode(code);
        return Context.Coupons.AnyAsync(c => c.Code == normalized && (excludeId == null || c.Id != excludeId), cancellationToken);
    }

    public async Task<PagedResult<CouponListItemDto>> SearchAsync(CouponQuery query, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var coupons = Context.Coupons.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var upper = term.ToUpperInvariant();
            coupons = coupons.Where(c => c.Code.Contains(upper) || c.Name.Contains(term));
        }

        // Same precedence as Coupon.GetStatus: inactive > expired > exhausted > scheduled > running.
        coupons = query.Status switch
        {
            CouponStatus.Inactive => coupons.Where(c => !c.IsActive),
            CouponStatus.Expired => coupons.Where(c => c.IsActive && c.EndsAt != null && c.EndsAt < utcNow),
            CouponStatus.Exhausted => coupons.Where(c => c.IsActive && (c.EndsAt == null || c.EndsAt >= utcNow)
                && c.UsageLimit != null && c.UsedCount >= c.UsageLimit),
            CouponStatus.Scheduled => coupons.Where(c => c.IsActive && (c.EndsAt == null || c.EndsAt >= utcNow)
                && (c.UsageLimit == null || c.UsedCount < c.UsageLimit) && c.StartsAt != null && c.StartsAt > utcNow),
            CouponStatus.Running => coupons.Where(c => c.IsActive && (c.EndsAt == null || c.EndsAt >= utcNow)
                && (c.UsageLimit == null || c.UsedCount < c.UsageLimit) && (c.StartsAt == null || c.StartsAt <= utcNow)),
            _ => coupons
        };

        var total = await coupons.CountAsync(cancellationToken);
        var rows = await Project(coupons
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<CouponListItemDto>(rows.Select(r => r.ToDto(utcNow)).ToList(), total, query.Page, query.PageSize);
    }

    public async Task<CouponListItemDto?> GetListItemAsync(int id, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var row = await Project(Context.Coupons.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(cancellationToken);
        return row?.ToDto(utcNow);
    }

    public async Task<(int OrderCount, decimal Revenue)> GetOrderStatsAsync(int couponId, CancellationToken cancellationToken = default)
    {
        var orders = Context.Orders.AsNoTracking().Where(o => o.CouponId == couponId);
        var count = await orders.CountAsync(cancellationToken);
        var revenue = await orders
            .Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Refunded)
            .SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0;
        return (count, revenue);
    }

    public async Task<IReadOnlyList<CouponOrderDto>> GetRecentOrdersAsync(int couponId, int take, CancellationToken cancellationToken = default) =>
        await Context.Orders.AsNoTracking()
            .Where(o => o.CouponId == couponId)
            .OrderByDescending(o => o.PlacedAt)
            .Take(take)
            .Select(o => new CouponOrderDto(o.Id, o.OrderCode, o.CustomerName, o.CustomerEmail, o.Status, o.DiscountAmount, o.TotalAmount, o.PlacedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Coupon>> ListOfferableAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        await Context.Coupons.AsNoTracking()
            .Where(c => c.IsPublic && c.IsActive
                && (c.StartsAt == null || c.StartsAt <= utcNow)
                && (c.EndsAt == null || c.EndsAt >= utcNow)
                && (c.UsageLimit == null || c.UsedCount < c.UsageLimit))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<int, int>> CountUsagesByUserAsync(IReadOnlyCollection<int> couponIds, string userId, CancellationToken cancellationToken = default)
    {
        if (couponIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        return await Context.CouponUsages.AsNoTracking()
            .Where(u => u.UserId == userId && couponIds.Contains(u.CouponId))
            .GroupBy(u => u.CouponId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    public Task ReplaceCartCouponCodeAsync(string oldCode, string? newCode, CancellationToken cancellationToken = default) =>
        Context.Carts
            .Where(c => c.CouponCode == oldCode)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CouponCode, newCode), cancellationToken);

    private static IQueryable<CouponRow> Project(IQueryable<Coupon> coupons) =>
        coupons.Select(c => new CouponRow
        {
            Id = c.Id, Code = c.Code, Name = c.Name, DiscountType = c.DiscountType, DiscountValue = c.DiscountValue,
            MaxDiscountAmount = c.MaxDiscountAmount, MinOrderAmount = c.MinOrderAmount, StartsAt = c.StartsAt, EndsAt = c.EndsAt,
            UsageLimit = c.UsageLimit, UsageLimitPerUser = c.UsageLimitPerUser, UsedCount = c.UsedCount, IsActive = c.IsActive,
            IsPublic = c.IsPublic, TotalDiscount = c.Usages.Sum(u => (decimal?)u.DiscountAmount) ?? 0
        });

    private sealed class CouponRow
    {
        public int Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public DiscountType DiscountType { get; init; }
        public decimal DiscountValue { get; init; }
        public decimal? MaxDiscountAmount { get; init; }
        public decimal MinOrderAmount { get; init; }
        public DateTime? StartsAt { get; init; }
        public DateTime? EndsAt { get; init; }
        public int? UsageLimit { get; init; }
        public int? UsageLimitPerUser { get; init; }
        public int UsedCount { get; init; }
        public bool IsActive { get; init; }
        public bool IsPublic { get; init; }
        public decimal TotalDiscount { get; init; }

        public CouponListItemDto ToDto(DateTime utcNow)
        {
            var status = new Coupon { IsActive = IsActive, StartsAt = StartsAt, EndsAt = EndsAt, UsageLimit = UsageLimit, UsedCount = UsedCount }
                .GetStatus(utcNow);
            return new CouponListItemDto(Id, Code, Name, DiscountType, DiscountValue, MaxDiscountAmount, MinOrderAmount, StartsAt, EndsAt,
                UsageLimit, UsageLimitPerUser, UsedCount, IsActive, IsPublic, status, TotalDiscount);
        }
    }
}

/// <summary>Single-statement stock updates: safe under concurrency without read-modify-write races.</summary>
public sealed class InventoryRepository(ApplicationDbContext context) : IInventoryRepository
{
    public async Task<bool> TryReserveAsync(int variantId, int quantity, CancellationToken cancellationToken = default)
    {
        var version = Guid.NewGuid();
        var updated = await context.ProductVariants.IgnoreQueryFilters()
            .Where(v => v.Id == variantId && v.IsActive && v.StockQuantity >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.StockQuantity, v => v.StockQuantity - quantity)
                .SetProperty(v => v.Version, version), cancellationToken);
        return updated == 1;
    }

    public Task ReleaseAsync(int variantId, int quantity, CancellationToken cancellationToken = default)
    {
        var version = Guid.NewGuid();
        return context.ProductVariants.IgnoreQueryFilters()
            .Where(v => v.Id == variantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.StockQuantity, v => v.StockQuantity + quantity)
                .SetProperty(v => v.Version, version), cancellationToken);
    }

    public Task AdjustProductAsync(int productId, int stockDelta, int soldDelta, CancellationToken cancellationToken = default) =>
        context.Products.IgnoreQueryFilters()
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.StockQuantity, p => p.StockQuantity + stockDelta < 0 ? 0 : p.StockQuantity + stockDelta)
                .SetProperty(p => p.SoldCount, p => p.SoldCount + soldDelta < 0 ? 0 : p.SoldCount + soldDelta), cancellationToken);
}

public sealed class WishlistRepository(ApplicationDbContext context) : EfRepository<Wishlist>(context), IWishlistRepository
{
    public Task<Wishlist?> GetByUserAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.Wishlists.Include(w => w.Items).FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<int>> GetProductIdsAsync(string userId, CancellationToken cancellationToken = default) =>
        await Context.WishlistItems.AsNoTracking()
            .Where(i => i.Wishlist.UserId == userId)
            .OrderByDescending(i => i.AddedAt)
            .Select(i => i.ProductId)
            .ToListAsync(cancellationToken);
}
