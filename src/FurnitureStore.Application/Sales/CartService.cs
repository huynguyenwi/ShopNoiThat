using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Application.Sales;

public interface ICartService
{
    Task<CartDto> GetAsync(CartOwner owner, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CartOwner owner, CancellationToken cancellationToken = default);
    Task<CartDto> AddAsync(CartOwner owner, int variantId, int quantity, CancellationToken cancellationToken = default);
    Task<CartDto> UpdateQuantityAsync(CartOwner owner, int itemId, int quantity, CancellationToken cancellationToken = default);
    Task<CartDto> RemoveAsync(CartOwner owner, int itemId, CancellationToken cancellationToken = default);
    Task<CartDto> ApplyCouponAsync(CartOwner owner, string code, CancellationToken cancellationToken = default);
    Task<CartDto> RemoveCouponAsync(CartOwner owner, CancellationToken cancellationToken = default);

    /// <summary>Public coupons usable now, best first, each marked as usable (or why not) for this cart.</summary>
    Task<IReadOnlyList<CouponOfferDto>> GetOffersAsync(CartOwner owner, CartDto cart, CancellationToken cancellationToken = default);

    /// <summary>Moves a guest cart into the user's cart after sign-in.</summary>
    Task MergeAsync(string anonymousId, string userId, CancellationToken cancellationToken = default);
}

public sealed class CartService(
    ICartRepository carts,
    ICouponRepository coupons,
    IRepository<ProductVariant> variants,
    IRepository<Product> products,
    IShippingCalculator shipping,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<CartService> logger) : ICartService
{
    public async Task<CartDto> GetAsync(CartOwner owner, CancellationToken cancellationToken = default)
    {
        if (owner.IsEmpty)
        {
            return CartDto.Empty(shipping.FreeShippingThreshold);
        }

        var cart = await carts.GetAsync(owner, cancellationToken);
        return cart is null ? CartDto.Empty(shipping.FreeShippingThreshold) : await BuildAsync(cart, owner, cancellationToken);
    }

    public Task<int> CountAsync(CartOwner owner, CancellationToken cancellationToken = default) =>
        owner.IsEmpty ? Task.FromResult(0) : carts.CountItemsAsync(owner, cancellationToken);

    public async Task<CartDto> AddAsync(CartOwner owner, int variantId, int quantity, CancellationToken cancellationToken = default)
    {
        EnsureOwner(owner);
        if (quantity is < 1 or > Cart.MaxQuantityPerItem)
        {
            throw new AppValidationException($"Số lượng phải từ 1 đến {Cart.MaxQuantityPerItem}.");
        }

        var variant = await variants.GetByIdAsync(variantId, cancellationToken) ?? throw new NotFoundException("Sản phẩm không tồn tại hoặc đã ngừng bán.");
        var product = await products.GetByIdAsync(variant.ProductId, cancellationToken);
        if (product is null || product.Status != ProductStatus.Active || !variant.IsActive)
        {
            throw new BusinessRuleException("Sản phẩm này hiện không còn bán.");
        }

        var cart = await carts.GetAsync(owner, cancellationToken);
        if (cart is null)
        {
            cart = new Cart { UserId = owner.UserId, AnonymousId = owner.UserId is null ? owner.AnonymousId : null };
            await carts.AddAsync(cart, cancellationToken);
        }

        var alreadyInCart = cart.Items.FirstOrDefault(i => i.ProductVariantId == variantId)?.Quantity ?? 0;
        if (alreadyInCart + quantity > variant.StockQuantity)
        {
            throw new BusinessRuleException(variant.StockQuantity == 0
                ? $"\"{product.Name} - {variant.Name}\" đã hết hàng."
                : $"\"{product.Name} - {variant.Name}\" chỉ còn {variant.StockQuantity} sản phẩm (giỏ hàng đang có {alreadyInCart}).");
        }

        cart.AddItem(variantId, quantity, timeProvider.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await BuildAsync(cart, owner, cancellationToken);
    }

    public async Task<CartDto> UpdateQuantityAsync(CartOwner owner, int itemId, int quantity, CancellationToken cancellationToken = default)
    {
        var cart = await GetRequiredCartAsync(owner, cancellationToken);
        var item = cart.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new NotFoundException("Sản phẩm không có trong giỏ hàng.");

        if (quantity > Cart.MaxQuantityPerItem)
        {
            throw new AppValidationException($"Số lượng tối đa mỗi sản phẩm là {Cart.MaxQuantityPerItem}.");
        }

        if (quantity > 0)
        {
            var variant = await variants.GetByIdAsync(item.ProductVariantId, cancellationToken);
            if (variant is null || quantity > variant.StockQuantity)
            {
                throw new BusinessRuleException($"Chỉ còn {variant?.StockQuantity ?? 0} sản phẩm trong kho.");
            }
        }

        cart.SetQuantity(item.ProductVariantId, quantity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await BuildAsync(cart, owner, cancellationToken);
    }

    public async Task<CartDto> RemoveAsync(CartOwner owner, int itemId, CancellationToken cancellationToken = default)
    {
        var cart = await GetRequiredCartAsync(owner, cancellationToken);
        var item = cart.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new NotFoundException("Sản phẩm không có trong giỏ hàng.");

        cart.RemoveItem(item.ProductVariantId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await BuildAsync(cart, owner, cancellationToken);
    }

    public async Task<CartDto> ApplyCouponAsync(CartOwner owner, string code, CancellationToken cancellationToken = default)
    {
        var cart = await GetRequiredCartAsync(owner, cancellationToken);
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is 0 or > 50)
        {
            throw new AppValidationException("Vui lòng nhập mã giảm giá.");
        }

        var coupon = await coupons.GetByCodeAsync(normalized, cancellationToken) ?? throw new BusinessRuleException("Mã giảm giá không tồn tại.");
        var lines = await carts.GetLinesAsync(cart.Id, cancellationToken);
        var subtotal = lines.Where(l => l.IsAvailable).Sum(l => l.LineTotal);
        var usedByUser = owner.UserId is null ? 0 : await coupons.CountUsageByUserAsync(coupon.Id, owner.UserId, cancellationToken);
        var reason = coupon.GetInvalidReason(subtotal, timeProvider.GetUtcNow().UtcDateTime, usedByUser);
        if (reason is not null)
        {
            throw new BusinessRuleException(reason);
        }

        cart.CouponCode = coupon.Code;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await BuildAsync(cart, owner, cancellationToken);
    }

    public const int MaxOffersShown = 5;

    public async Task<IReadOnlyList<CouponOfferDto>> GetOffersAsync(CartOwner owner, CartDto cart, CancellationToken cancellationToken = default)
    {
        if (cart.IsEmpty)
        {
            return [];
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var offerable = await coupons.ListOfferableAsync(now, cancellationToken);
        var usedByUser = owner.UserId is null
            ? new Dictionary<int, int>()
            : await coupons.CountUsagesByUserAsync(offerable.Select(c => c.Id).ToList(), owner.UserId, cancellationToken);

        return offerable
            .Select(c =>
            {
                var reason = c.GetInvalidReason(cart.Subtotal, now, usedByUser.GetValueOrDefault(c.Id));
                var missing = c.MinOrderAmount - cart.Subtotal;
                if (reason is not null && missing > 0)
                {
                    reason = $"Mua thêm {CouponText.Money(missing)} để dùng mã này.";
                }

                return new CouponOfferDto(c.Code, c.Name, c.Description,
                    CouponText.Benefit(c.DiscountType, c.DiscountValue, c.MaxDiscountAmount),
                    CouponText.Conditions(c.MinOrderAmount, c.UsageLimitPerUser, c.EndsAt),
                    IsEligible: reason is null, IsApplied: c.Code == cart.CouponCode, reason,
                    EstimatedDiscount: reason is null ? c.CalculateDiscount(cart.Subtotal) : 0);
            })
            .OrderByDescending(o => o.IsApplied)
            .ThenByDescending(o => o.IsEligible)
            .ThenByDescending(o => o.EstimatedDiscount)
            .ThenBy(o => o.Code, StringComparer.Ordinal)
            .Take(MaxOffersShown)
            .ToList();
    }

    public async Task<CartDto> RemoveCouponAsync(CartOwner owner, CancellationToken cancellationToken = default)
    {
        var cart = await GetRequiredCartAsync(owner, cancellationToken);
        cart.CouponCode = null;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await BuildAsync(cart, owner, cancellationToken);
    }

    public async Task MergeAsync(string anonymousId, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(anonymousId))
        {
            return;
        }

        var guestCart = await carts.GetAsync(new CartOwner(null, anonymousId), cancellationToken);
        if (guestCart is null)
        {
            return;
        }

        var userCart = await carts.GetAsync(new CartOwner(userId, null), cancellationToken);
        if (userCart is null)
        {
            // Simply hand the guest cart over to the user.
            guestCart.UserId = userId;
            guestCart.AnonymousId = null;
        }
        else
        {
            userCart.MergeFrom(guestCart, timeProvider.GetUtcNow().UtcDateTime);
            carts.Remove(guestCart);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Merged guest cart into cart of user {UserId}", userId);
    }

    // ------------------------------------------------------------------ helpers

    private static void EnsureOwner(CartOwner owner)
    {
        if (owner.IsEmpty)
        {
            throw new BusinessRuleException("Không xác định được giỏ hàng. Vui lòng bật cookie và thử lại.");
        }
    }

    private async Task<Cart> GetRequiredCartAsync(CartOwner owner, CancellationToken cancellationToken)
    {
        EnsureOwner(owner);
        return await carts.GetAsync(owner, cancellationToken) ?? throw new NotFoundException("Giỏ hàng trống.");
    }

    private async Task<CartDto> BuildAsync(Cart cart, CartOwner owner, CancellationToken cancellationToken)
    {
        var lines = await carts.GetLinesAsync(cart.Id, cancellationToken);
        var warnings = new List<string>();
        foreach (var line in lines)
        {
            if (!line.IsAvailable)
            {
                warnings.Add($"\"{line.ProductName}\" hiện không còn bán, vui lòng xóa khỏi giỏ hàng.");
            }
            else if (line.Stock == 0)
            {
                warnings.Add($"\"{line.ProductName} - {line.VariantName}\" đã hết hàng.");
            }
            else if (line.ExceedsStock)
            {
                warnings.Add($"\"{line.ProductName} - {line.VariantName}\" chỉ còn {line.Stock} sản phẩm.");
            }
        }

        var subtotal = lines.Where(l => l.IsAvailable).Sum(l => l.LineTotal);
        decimal discount = 0;
        string? couponMessage = null;

        if (!string.IsNullOrEmpty(cart.CouponCode))
        {
            var coupon = await coupons.GetByCodeAsync(cart.CouponCode, cancellationToken);
            if (coupon is null)
            {
                couponMessage = "Mã giảm giá không còn tồn tại.";
            }
            else
            {
                var usedByUser = owner.UserId is null ? 0 : await coupons.CountUsageByUserAsync(coupon.Id, owner.UserId, cancellationToken);
                couponMessage = coupon.GetInvalidReason(subtotal, timeProvider.GetUtcNow().UtcDateTime, usedByUser);
                if (couponMessage is null)
                {
                    discount = coupon.CalculateDiscount(subtotal);
                    couponMessage = $"Đã áp dụng mã {coupon.Code}: {coupon.Name}";
                }
            }
        }

        var shippingFee = lines.Count == 0 ? 0 : shipping.Calculate(subtotal - discount);
        return new CartDto(lines, subtotal, discount, shippingFee, Math.Max(0, subtotal - discount + shippingFee),
            cart.CouponCode, couponMessage, shipping.FreeShippingThreshold, warnings);
    }
}
