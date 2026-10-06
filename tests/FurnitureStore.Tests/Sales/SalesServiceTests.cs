using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Sales;

public sealed class SalesServiceTests : IAsyncLifetime
{
    private ServiceTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> action) => _host.RunAsync(action);
    private Task<T> Cart<T>(Func<ICartService, Task<T>> action) => Run(sp => action(sp.GetRequiredService<ICartService>()));
    private Task<T> Orders<T>(Func<IOrderService, Task<T>> action) => Run(sp => action(sp.GetRequiredService<IOrderService>()));
    private Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action) => Run(sp => action(sp.GetRequiredService<ApplicationDbContext>()));

    private async Task<string> CreateUserAsync(string? email = null)
    {
        email ??= $"u{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FullName = "Khách Test",
            SecurityStamp = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow
        };
        await Db(async db => { db.Users.Add(user); await db.SaveChangesAsync(); return 0; });
        return user.Id;
    }

    /// <summary>A variant of the given product SKU with at least <paramref name="minStock"/> units.</summary>
    private Task<ProductVariant> VariantAsync(string productSku, int minStock = 1) =>
        Db(db => db.ProductVariants.Include(v => v.Product).AsNoTracking()
            .FirstAsync(v => v.Product.Sku == productSku && v.StockQuantity >= minStock));

    private static CheckoutCommand Checkout(PaymentMethod method = PaymentMethod.COD) => new()
    {
        FullName = "Nguyễn Văn Test",
        Phone = "0912345678",
        Email = "test@example.com",
        AddressLine = "12 Lê Lợi",
        Ward = "Phường Bến Nghé",
        Province = "TP. Hồ Chí Minh",
        PaymentMethod = method,
        SaveAddress = true
    };

    // ------------------------------------------------------------------ Cart

    [Fact]
    public async Task Cart_AddUpdateRemove_ComputesTotals()
    {
        var userId = await CreateUserAsync();
        var owner = new CartOwner(userId, null);
        var chair = await VariantAsync("GA-CURVE", 3);

        var afterAdd = await Cart(c => c.AddAsync(owner, chair.Id, 2));
        Assert.Equal(2, afterAdd.TotalQuantity);
        Assert.Equal(chair.Price * 2, afterAdd.Subtotal);
        Assert.Equal(300_000, afterAdd.ShippingFee); // below the free-shipping threshold

        var line = Assert.Single(afterAdd.Items);
        var afterUpdate = await Cart(c => c.UpdateQuantityAsync(owner, line.ItemId, 3));
        Assert.Equal(3, afterUpdate.Items[0].Quantity);

        var afterRemove = await Cart(c => c.RemoveAsync(owner, line.ItemId));
        Assert.True(afterRemove.IsEmpty);
        Assert.Equal(0, afterRemove.Total);
    }

    [Fact]
    public async Task Cart_FreeShippingAboveThreshold()
    {
        var owner = new CartOwner(await CreateUserAsync(), null);
        var table = await VariantAsync("BA-WALNUT");

        var cart = await Cart(c => c.AddAsync(owner, table.Id, 1));

        Assert.True(cart.Subtotal >= 10_000_000);
        Assert.Equal(0, cart.ShippingFee);
    }

    [Fact]
    public async Task Cart_RejectsMoreThanStock_AndInactiveProducts()
    {
        var owner = new CartOwner(await CreateUserAsync(), null);
        var variant = await VariantAsync("GA-CURVE");

        var tooMany = await Assert.ThrowsAsync<BusinessRuleException>(() => Cart(c => c.AddAsync(owner, variant.Id, variant.StockQuantity + 1)));
        Assert.Contains("chỉ còn", tooMany.Message);

        var soldOut = await Db(db => db.ProductVariants.FirstAsync(v => v.StockQuantity == 0));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Cart(c => c.AddAsync(owner, soldOut.Id, 1)));

        await Db(async db => { (await db.Products.SingleAsync(p => p.Id == variant.ProductId)).Status = ProductStatus.Inactive; return await db.SaveChangesAsync(); });
        await Assert.ThrowsAsync<BusinessRuleException>(() => Cart(c => c.AddAsync(owner, variant.Id, 1)));
    }

    [Fact]
    public async Task Cart_GuestCartIsMergedIntoUserCartOnSignIn()
    {
        var userId = await CreateUserAsync();
        var guest = new CartOwner(null, Guid.NewGuid().ToString("N"));
        var chair = await VariantAsync("GA-CURVE", 5);
        var shelf = await VariantAsync("KTT-PINE", 2);

        await Cart(c => c.AddAsync(new CartOwner(userId, null), chair.Id, 1));
        await Cart(c => c.AddAsync(guest, chair.Id, 2));
        await Cart(c => c.AddAsync(guest, shelf.Id, 1));

        await Cart(async c => { await c.MergeAsync(guest.AnonymousId!, userId); return 0; });

        var merged = await Cart(c => c.GetAsync(new CartOwner(userId, null)));
        Assert.Equal(3, merged.Items.Single(i => i.VariantId == chair.Id).Quantity);
        Assert.Equal(1, merged.Items.Single(i => i.VariantId == shelf.Id).Quantity);
        Assert.True((await Cart(c => c.GetAsync(guest))).IsEmpty);
    }

    [Fact]
    public async Task Coupon_ValidAppliesDiscount_InvalidOnesAreRejected()
    {
        var owner = new CartOwner(await CreateUserAsync(), null);
        var chair = await VariantAsync("GA-CURVE", 1);
        await Cart(c => c.AddAsync(owner, chair.Id, 1));

        var tooSmall = await Assert.ThrowsAsync<BusinessRuleException>(() => Cart(c => c.ApplyCouponAsync(owner, "chaoban10")));
        Assert.Contains("tối thiểu", tooSmall.Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Cart(c => c.ApplyCouponAsync(owner, "HETHAN")));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Cart(c => c.ApplyCouponAsync(owner, "KHONGCO")));

        var table = await VariantAsync("BA-WALNUT");
        await Cart(c => c.AddAsync(owner, table.Id, 1));
        var cart = await Cart(c => c.ApplyCouponAsync(owner, "chaoban10"));

        Assert.Equal("CHAOBAN10", cart.CouponCode);
        Assert.Equal(Math.Min(Math.Round(cart.Subtotal * 0.1m), 2_000_000), cart.DiscountAmount);
        Assert.Equal(cart.Subtotal - cart.DiscountAmount + cart.ShippingFee, cart.Total);
    }

    // ------------------------------------------------------------------ Orders

    [Fact]
    public async Task PlaceOrder_CreatesOrder_ReservesStock_ClearsCart_AndNotifies()
    {
        var userId = await CreateUserAsync();
        var owner = new CartOwner(userId, null);
        var table = await VariantAsync("BA-WALNUT", 2);
        var soldBefore = await Db(db => db.Products.Where(p => p.Id == table.ProductId).Select(p => p.SoldCount).SingleAsync());
        await Cart(c => c.AddAsync(owner, table.Id, 2));
        await Cart(c => c.ApplyCouponAsync(owner, "CHAOBAN10"));

        var result = await Orders(o => o.PlaceOrderAsync(userId, Checkout()));

        Assert.Matches("^DH\\d{6}-[A-Z2-9]{5}$", result.OrderCode);
        var order = await Orders(o => o.GetMyOrderAsync(userId, result.OrderCode));
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Equal(table.Price * 2, order.Subtotal);
        Assert.Equal(2_000_000, order.DiscountAmount);           // 10% capped at 2M
        Assert.Equal(0, order.ShippingFee);                      // free above 10M
        Assert.Equal(order.Subtotal - order.DiscountAmount, order.TotalAmount);
        Assert.Equal(table.Sku, Assert.Single(order.Items).Sku);
        Assert.Equal("TP. Hồ Chí Minh", order.ShippingAddress!.Province);
        Assert.Single(order.History);
        Assert.Equal(PaymentMethod.COD, Assert.Single(order.Payments).Method);

        Assert.Equal(table.StockQuantity - 2, await Db(db => db.ProductVariants.Where(v => v.Id == table.Id).Select(v => v.StockQuantity).SingleAsync()));
        Assert.Equal(soldBefore + 2, await Db(db => db.Products.Where(p => p.Id == table.ProductId).Select(p => p.SoldCount).SingleAsync()));
        Assert.True((await Cart(c => c.GetAsync(owner))).IsEmpty);
        Assert.Equal(1, await Db(db => db.CouponUsages.CountAsync(u => u.UserId == userId)));
        Assert.Equal(1, await Db(db => db.Coupons.Where(c => c.Code == "CHAOBAN10").Select(c => c.UsedCount).SingleAsync()));
        Assert.True(await Db(db => db.Notifications.AnyAsync(n => n.RecipientRole == AppRoles.Admin && n.Type == NotificationType.OrderPlaced)));
        Assert.True(await Db(db => db.CustomerAddresses.AnyAsync(a => a.UserId == userId && a.IsDefault)));
        Assert.Contains(_host.Emails.Sent, e => e.Subject.Contains(result.OrderCode) && e.To == "test@example.com");
    }

    [Fact]
    public async Task PlaceOrder_WithBankTransfer_ReturnsTransferInstructions()
    {
        var userId = await CreateUserAsync();
        await Cart(async c => await c.AddAsync(new CartOwner(userId, null), (await VariantAsync("KTT-PINE")).Id, 1));

        var result = await Orders(o => o.PlaceOrderAsync(userId, Checkout(PaymentMethod.BankTransfer)));

        Assert.NotNull(result.PaymentInstructions);
        Assert.Equal($"NHAMOC {result.OrderCode}", result.PaymentInstructions.TransferNote);
        Assert.Equal(result.Total, result.PaymentInstructions.Amount);
        var order = await Orders(o => o.GetMyOrderAsync(userId, result.OrderId));
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
    }

    [Fact]
    public async Task PlaceOrder_LastItemRace_SecondBuyerFails_AndNothingIsLeftHalfDone()
    {
        var variant = await VariantAsync("TDG-WAL");
        await Db(async db => { (await db.ProductVariants.SingleAsync(v => v.Id == variant.Id)).StockQuantity = 1; return await db.SaveChangesAsync(); });

        var first = await CreateUserAsync();
        var second = await CreateUserAsync();
        await Cart(c => c.AddAsync(new CartOwner(first, null), variant.Id, 1));
        await Cart(c => c.AddAsync(new CartOwner(second, null), variant.Id, 1)); // both saw 1 in stock

        await Orders(o => o.PlaceOrderAsync(first, Checkout()));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Orders(o => o.PlaceOrderAsync(second, Checkout())));

        Assert.Contains("không đủ hàng", ex.Message);
        Assert.Equal(0, await Db(db => db.ProductVariants.Where(v => v.Id == variant.Id).Select(v => v.StockQuantity).SingleAsync()));
        Assert.Equal(1, await Db(db => db.Orders.CountAsync(o => o.UserId == first || o.UserId == second)));
        Assert.False((await Cart(c => c.GetAsync(new CartOwner(second, null)))).IsEmpty); // rolled back, cart intact
    }

    [Fact]
    public async Task PlaceOrder_Validation_AndEmptyCart()
    {
        var userId = await CreateUserAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => Orders(o => o.PlaceOrderAsync(userId, Checkout())));

        var invalid = Checkout();
        invalid.Phone = "123";
        invalid.Province = "Tỉnh không tồn tại";
        invalid.Email = "khong-phai-email";
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Orders(o => o.PlaceOrderAsync(userId, invalid)));
        Assert.True(ex.FieldErrors.ContainsKey("Phone"));
        Assert.True(ex.FieldErrors.ContainsKey("Province"));
        Assert.True(ex.FieldErrors.ContainsKey("Email"));
    }

    [Fact]
    public async Task CancelOrder_RestocksReleasesCouponAndFailsPayment()
    {
        var userId = await CreateUserAsync();
        var table = await VariantAsync("BA-WALNUT", 1);
        await Cart(c => c.AddAsync(new CartOwner(userId, null), table.Id, 1));
        await Cart(c => c.ApplyCouponAsync(new CartOwner(userId, null), "GIAM500K"));
        var placed = await Orders(o => o.PlaceOrderAsync(userId, Checkout()));

        await Orders(async o => { await o.CancelMyOrderAsync(userId, placed.OrderCode, "Đổi ý"); return 0; });

        var order = await Orders(o => o.GetMyOrderAsync(userId, placed.OrderCode));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Đổi ý", order.CancelReason);
        Assert.Equal(PaymentStatus.Failed, order.PaymentStatus);
        Assert.Equal(2, order.History.Count);
        Assert.Equal(table.StockQuantity, await Db(db => db.ProductVariants.Where(v => v.Id == table.Id).Select(v => v.StockQuantity).SingleAsync()));
        Assert.Equal(0, await Db(db => db.Coupons.Where(c => c.Code == "GIAM500K").Select(c => c.UsedCount).SingleAsync()));
        Assert.Equal(0, await Db(db => db.CouponUsages.CountAsync(u => u.UserId == userId)));

        // A cancelled order cannot be cancelled again.
        await Assert.ThrowsAsync<BusinessRuleException>(() => Orders(async o => { await o.CancelMyOrderAsync(userId, placed.OrderCode, null); return 0; }));
    }

    [Fact]
    public async Task CancelOrder_NotAllowedOnceProcessing()
    {
        var userId = await CreateUserAsync();
        await Cart(async c => await c.AddAsync(new CartOwner(userId, null), (await VariantAsync("KTT-PINE")).Id, 1));
        var placed = await Orders(o => o.PlaceOrderAsync(userId, Checkout()));
        await Db(async db =>
        {
            var order = await db.Orders.SingleAsync(o => o.Id == placed.OrderId);
            order.Status = OrderStatus.Processing;
            return await db.SaveChangesAsync();
        });

        await Assert.ThrowsAsync<BusinessRuleException>(() => Orders(async o => { await o.CancelMyOrderAsync(userId, placed.OrderCode, null); return 0; }));
    }

    [Fact]
    public async Task Orders_OfAnotherCustomer_AreNotVisible()
    {
        var owner = await CreateUserAsync();
        var stranger = await CreateUserAsync();
        await Cart(async c => await c.AddAsync(new CartOwner(owner, null), (await VariantAsync("KTT-PINE")).Id, 1));
        var placed = await Orders(o => o.PlaceOrderAsync(owner, Checkout()));

        await Assert.ThrowsAsync<NotFoundException>(() => Orders(o => o.GetMyOrderAsync(stranger, placed.OrderCode)));
        await Assert.ThrowsAsync<NotFoundException>(() => Orders(o => o.GetMyOrderAsync(stranger, placed.OrderId)));
        await Assert.ThrowsAsync<NotFoundException>(() => Orders(async o => { await o.CancelMyOrderAsync(stranger, placed.OrderCode, null); return 0; }));
        Assert.Empty((await Orders(o => o.GetMyOrdersAsync(stranger))).Items);
        Assert.Single((await Orders(o => o.GetMyOrdersAsync(owner))).Items);
    }

    [Fact]
    public async Task Coupon_PerUserLimit_AppliesAfterFirstOrder()
    {
        var userId = await CreateUserAsync();
        var owner = new CartOwner(userId, null);
        await Cart(async c => await c.AddAsync(owner, (await VariantAsync("BA-WALNUT")).Id, 1));
        await Cart(c => c.ApplyCouponAsync(owner, "CHAOBAN10"));
        await Orders(o => o.PlaceOrderAsync(userId, Checkout()));

        await Cart(async c => await c.AddAsync(owner, (await VariantAsync("BA-SCANDI")).Id, 1));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Cart(c => c.ApplyCouponAsync(owner, "CHAOBAN10")));

        Assert.Contains("hết lượt", ex.Message);
    }

    // ------------------------------------------------------------------ Wishlist & addresses

    [Fact]
    public async Task Wishlist_ToggleAddsAndRemoves()
    {
        var userId = await CreateUserAsync();
        var productId = (await VariantAsync("SF-OSLO")).ProductId;

        Assert.True(await Run(sp => sp.GetRequiredService<IWishlistService>().ToggleAsync(userId, productId)));
        Assert.Single(await Run(sp => sp.GetRequiredService<IWishlistService>().GetAsync(userId)));
        Assert.False(await Run(sp => sp.GetRequiredService<IWishlistService>().ToggleAsync(userId, productId)));
        Assert.Empty(await Run(sp => sp.GetRequiredService<IWishlistService>().GetProductIdsAsync(userId)));
        await Assert.ThrowsAsync<NotFoundException>(() => Run(sp => sp.GetRequiredService<IWishlistService>().ToggleAsync(userId, 999_999)));
    }

    [Fact]
    public async Task Addresses_CrudDefaultAndOwnership()
    {
        var userId = await CreateUserAsync();
        var otherUser = await CreateUserAsync();
        var command = new CustomerAddressCommand { RecipientName = "A", Phone = "0912345678", AddressLine = "1 Đường A", Ward = "Phường 1", Province = "TP. Hà Nội" };

        var first = await Run(sp => sp.GetRequiredService<IAddressService>().CreateAsync(userId, command));
        var second = await Run(sp => sp.GetRequiredService<IAddressService>().CreateAsync(userId, new CustomerAddressCommand
        {
            RecipientName = "B", Phone = "0987654321", AddressLine = "2 Đường B", Ward = "Phường 2", Province = "TP. Đà Nẵng", IsDefault = true
        }));

        var list = await Run(sp => sp.GetRequiredService<IAddressService>().ListAsync(userId));
        Assert.Equal(second, Assert.Single(list, a => a.IsDefault).Id);

        await Run(async sp => { await sp.GetRequiredService<IAddressService>().DeleteAsync(userId, second); return 0; });
        Assert.Equal(first, Assert.Single(await Run(sp => sp.GetRequiredService<IAddressService>().ListAsync(userId))).Id);
        Assert.True((await Run(sp => sp.GetRequiredService<IAddressService>().GetAsync(userId, first))).IsDefault);

        await Assert.ThrowsAsync<NotFoundException>(() => Run(sp => sp.GetRequiredService<IAddressService>().GetAsync(otherUser, first)));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(sp => sp.GetRequiredService<IAddressService>().CreateAsync(userId, new CustomerAddressCommand())));
    }
}
