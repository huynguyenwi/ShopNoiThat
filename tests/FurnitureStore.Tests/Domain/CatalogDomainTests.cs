using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Domain.Exceptions;

namespace FurnitureStore.Tests.Domain;

public sealed class CatalogDomainTests
{
    [Fact]
    public void DecreaseStock_ReducesQuantity()
    {
        var variant = new ProductVariant { Name = "Nâu", StockQuantity = 5 };

        variant.DecreaseStock(3);

        Assert.Equal(2, variant.StockQuantity);
    }

    [Fact]
    public void DecreaseStock_BeyondAvailable_Throws()
    {
        var variant = new ProductVariant { Name = "Nâu", StockQuantity = 2 };

        var exception = Assert.Throws<DomainException>(() => variant.DecreaseStock(3));

        Assert.Contains("chỉ còn 2", exception.Message);
        Assert.Equal(2, variant.StockQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StockChanges_RequirePositiveQuantity(int quantity)
    {
        var variant = new ProductVariant { StockQuantity = 5 };

        Assert.Throws<DomainException>(() => variant.DecreaseStock(quantity));
        Assert.Throws<DomainException>(() => variant.IncreaseStock(quantity));
    }

    [Fact]
    public void Variant_DiscountPercent_IsComputedFromOriginalPrice()
    {
        var variant = new ProductVariant { Price = 8_500_000, OriginalPrice = 10_000_000 };

        Assert.True(variant.IsOnSale);
        Assert.Equal(15, variant.DiscountPercent);
    }

    [Fact]
    public void SyncFromVariants_UsesCheapestActiveVariant_AndSumsStock()
    {
        var product = new Product();
        product.Variants.Add(new ProductVariant { Price = 12_000_000, StockQuantity = 3, IsActive = true });
        product.Variants.Add(new ProductVariant { Price = 9_000_000, OriginalPrice = 10_000_000, StockQuantity = 2, IsActive = true });
        product.Variants.Add(new ProductVariant { Price = 1_000, StockQuantity = 50, IsActive = false });

        product.SyncFromVariants();

        Assert.Equal(5, product.StockQuantity);
        Assert.Equal(10_000_000, product.BasePrice);
        Assert.Equal(9_000_000, product.DiscountPrice);
        Assert.Equal(9_000_000, product.EffectivePrice);
        Assert.Equal(10, product.DiscountPercent);
    }

    [Fact]
    public void Product_WithoutValidDiscount_IsNotOnSale()
    {
        var product = new Product { BasePrice = 5_000_000, DiscountPrice = 6_000_000 };

        Assert.False(product.IsOnSale);
        Assert.Equal(5_000_000, product.EffectivePrice);
        Assert.Equal(0, product.DiscountPercent);
    }

    [Fact]
    public void ApplyRatingSummary_ClampsAndRounds()
    {
        var product = new Product();

        product.ApplyRatingSummary(4.666m, 3);
        Assert.Equal(4.67m, product.AverageRating);

        product.ApplyRatingSummary(3m, 0);
        Assert.Equal(0m, product.AverageRating);
    }

    [Fact]
    public void Cart_AddItem_MergesSameVariant_AndCapsQuantity()
    {
        var now = DateTime.UtcNow;
        var cart = new Cart();

        cart.AddItem(10, 2, now);
        cart.AddItem(10, 3, now);
        cart.AddItem(11, 150, now);

        Assert.Equal(2, cart.Items.Count);
        Assert.Equal(5, cart.Items.Single(i => i.ProductVariantId == 10).Quantity);
        Assert.Equal(Cart.MaxQuantityPerItem, cart.Items.Single(i => i.ProductVariantId == 11).Quantity);
    }

    [Fact]
    public void Cart_SetQuantityZero_RemovesItem()
    {
        var cart = new Cart();
        cart.AddItem(10, 2, DateTime.UtcNow);

        cart.SetQuantity(10, 0);

        Assert.Empty(cart.Items);
    }

    [Fact]
    public void Cart_MergeFrom_CombinesGuestCart()
    {
        var now = DateTime.UtcNow;
        var userCart = new Cart { UserId = "u1" };
        userCart.AddItem(1, 1, now);
        var guestCart = new Cart { AnonymousId = "guest", CouponCode = "HELLO" };
        guestCart.AddItem(1, 2, now);
        guestCart.AddItem(2, 1, now);

        userCart.MergeFrom(guestCart, now);

        Assert.Equal(3, userCart.Items.Single(i => i.ProductVariantId == 1).Quantity);
        Assert.Equal(4, userCart.TotalQuantity);
        Assert.Equal("HELLO", userCart.CouponCode);
    }

    [Fact]
    public void Wishlist_Add_IgnoresDuplicates()
    {
        var wishlist = new Wishlist();

        Assert.True(wishlist.Add(5, DateTime.UtcNow));
        Assert.False(wishlist.Add(5, DateTime.UtcNow));
        Assert.Single(wishlist.Items);
    }

    [Theory]
    [InlineData(DiscountType.Percentage, 10, null, 20_000_000, 2_000_000)]
    [InlineData(DiscountType.Percentage, 10, 1_000_000, 20_000_000, 1_000_000)]
    [InlineData(DiscountType.FixedAmount, 500_000, null, 20_000_000, 500_000)]
    [InlineData(DiscountType.FixedAmount, 500_000, null, 300_000, 300_000)]
    public void Coupon_CalculateDiscount(DiscountType type, decimal value, int? cap, decimal subtotal, decimal expected)
    {
        var coupon = new Coupon { DiscountType = type, DiscountValue = value, MaxDiscountAmount = cap };

        Assert.Equal(expected, coupon.CalculateDiscount(subtotal));
    }

    [Fact]
    public void Coupon_Validation_ChecksActivePeriodUsageAndMinimum()
    {
        var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var coupon = new Coupon
        {
            IsActive = true,
            StartsAt = now.AddDays(-1),
            EndsAt = now.AddDays(10),
            MinOrderAmount = 5_000_000,
            UsageLimit = 100,
            UsedCount = 10,
            UsageLimitPerUser = 1
        };

        Assert.Null(coupon.GetInvalidReason(6_000_000, now, usedByThisUser: 0));
        Assert.NotNull(coupon.GetInvalidReason(4_000_000, now, 0));
        Assert.NotNull(coupon.GetInvalidReason(6_000_000, now.AddDays(11), 0));
        Assert.NotNull(coupon.GetInvalidReason(6_000_000, now, usedByThisUser: 1));

        coupon.UsedCount = 100;
        Assert.NotNull(coupon.GetInvalidReason(6_000_000, now, 0));
    }

    [Fact]
    public void PriceRule_IsEffectiveOnlyInsideItsPeriod()
    {
        var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var rule = new PriceRule { IsActive = true, EffectiveFrom = now.AddDays(-1), EffectiveTo = now.AddDays(1) };

        Assert.True(rule.IsEffectiveAt(now));
        Assert.False(rule.IsEffectiveAt(now.AddDays(2)));

        rule.IsActive = false;
        Assert.False(rule.IsEffectiveAt(now));
    }
}
