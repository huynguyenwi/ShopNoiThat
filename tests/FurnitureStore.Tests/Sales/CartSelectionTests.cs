using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Admin;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Sales;

/// <summary>Ticking cart lines: only ticked lines are totalled and ordered, the others stay in the cart.</summary>
public sealed class CartSelectionTests : IAsyncLifetime
{
    private ServiceTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> action) => _host.RunAsync(action);
    private Task<T> Cart<T>(Func<ICartService, Task<T>> action) => Run(sp => action(sp.GetRequiredService<ICartService>()));
    private Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action) => Run(sp => action(sp.GetRequiredService<ApplicationDbContext>()));

    private Task<ProductVariant> VariantAsync(string productSku, int minStock = 2) =>
        Db(db => db.ProductVariants.AsNoTracking().FirstAsync(v => v.Product.Sku == productSku && v.StockQuantity >= minStock));

    private static CheckoutCommand Checkout() => new()
    {
        FullName = "Nguyễn Văn Chọn", Phone = "0912345678", Email = "chon@example.com", AddressLine = "1 Lê Lợi",
        Ward = "Phường Sài Gòn", Province = "TP. Hồ Chí Minh"
    };

    /// <summary>A customer with a chair and a dining set in the cart.</summary>
    private async Task<(CartOwner Owner, string UserId, ProductVariant Chair, ProductVariant Set)> TwoLinesAsync()
    {
        var userId = await AdminServiceTests.CreateCustomerAsync(_host);
        var owner = new CartOwner(userId, null);
        var chair = await VariantAsync("GA-CURVE");
        var set = await VariantAsync("BBA-ANGIA");
        await Cart(c => c.AddAsync(owner, chair.Id, 1));
        await Cart(c => c.AddAsync(owner, set.Id, 1));
        return (owner, userId, chair, set);
    }

    private static int LineOf(CartDto cart, int variantId) => cart.Items.Single(i => i.VariantId == variantId).ItemId;

    [Fact]
    public async Task NewLines_AreTicked_AndOnlyTickedLinesAreTotalled()
    {
        var (owner, _, chair, set) = await TwoLinesAsync();
        var both = await Cart(c => c.GetAsync(owner));
        Assert.All(both.Items, i => Assert.True(i.IsSelected));
        Assert.True(both.AllSelected);
        Assert.Equal(chair.Price + set.Price, both.Total);

        var chairOnly = await Cart(c => c.SetSelectedAsync(owner, LineOf(both, set.Id), false));

        Assert.Equal(chair.Price, chairOnly.Subtotal);
        Assert.Equal(chair.Price, chairOnly.Total);
        Assert.Equal(1, chairOnly.SelectedQuantity);
        Assert.Equal(2, chairOnly.TotalQuantity);            // the set is still in the cart
        Assert.False(chairOnly.AllSelected);
        Assert.True(chairOnly.CanCheckout);

        var none = await Cart(c => c.SelectAllAsync(owner, false));
        Assert.False(none.HasSelection);
        Assert.False(none.CanCheckout);
        Assert.Equal(0, none.Total);

        var all = await Cart(c => c.SelectAllAsync(owner, true));
        Assert.True(all.AllSelected);
    }

    [Fact]
    public async Task Order_ContainsOnlyTheTickedLines_TheOthersStayInTheCart()
    {
        var (owner, userId, chair, set) = await TwoLinesAsync();
        var cart = await Cart(c => c.GetAsync(owner));
        await Cart(c => c.SetSelectedAsync(owner, LineOf(cart, chair.Id), false));

        var placed = await Run(sp => sp.GetRequiredService<IOrderService>().PlaceOrderAsync(userId, Checkout()));

        var order = await Run(sp => sp.GetRequiredService<IOrderService>().GetMyOrderAsync(userId, placed.OrderCode));
        Assert.Equal(set.Sku, Assert.Single(order.Items).Sku);
        Assert.Equal(set.Price, order.Subtotal);
        var left = await Cart(c => c.GetAsync(owner));
        var line = Assert.Single(left.Items);
        Assert.Equal(chair.Id, line.VariantId);
        Assert.False(line.IsSelected);
        Assert.Equal(chair.StockQuantity, await Db(db => db.ProductVariants.Where(v => v.Id == chair.Id).Select(v => v.StockQuantity).SingleAsync()));
    }

    [Fact]
    public async Task NothingTicked_CannotBeOrdered()
    {
        var (owner, userId, _, _) = await TwoLinesAsync();
        await Cart(c => c.SelectAllAsync(owner, false));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Run(sp => sp.GetRequiredService<IOrderService>().PlaceOrderAsync(userId, Checkout())));

        Assert.Contains("tích chọn", error.Message);
        Assert.Equal(2, (await Cart(c => c.GetAsync(owner))).Items.Count);
    }

    [Fact]
    public async Task OrderingFromTheProductPage_TicksThatProductAlone()
    {
        var (owner, _, chair, set) = await TwoLinesAsync();

        var cart = await Cart(c => c.AddAsync(owner, chair.Id, 1, selectOnly: true));

        Assert.True(cart.Items.Single(i => i.VariantId == chair.Id).IsSelected);
        Assert.False(cart.Items.Single(i => i.VariantId == set.Id).IsSelected);
        Assert.Equal(2, cart.SelectedQuantity);               // 2 chairs now

        // Adding a line again from the product page ticks it again.
        var again = await Cart(c => c.AddAsync(owner, set.Id, 1));
        Assert.True(again.AllSelected);
    }

    [Fact]
    public async Task SoldOutLine_CannotBeTicked_AndAnUntickedOneDoesNotBlockTheOrder()
    {
        var (owner, _, chair, set) = await TwoLinesAsync();
        await Db(async db => { (await db.ProductVariants.SingleAsync(v => v.Id == chair.Id)).StockQuantity = 0; return await db.SaveChangesAsync(); });

        var ticked = await Cart(c => c.GetAsync(owner));
        Assert.False(ticked.CanCheckout);                    // still ticked: it blocks with a warning
        Assert.Contains(ticked.Warnings, w => w.Contains("hết hàng"));

        var unticked = await Cart(c => c.SetSelectedAsync(owner, LineOf(ticked, chair.Id), false));
        Assert.True(unticked.CanCheckout);
        Assert.Empty(unticked.Warnings);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Cart(c => c.SetSelectedAsync(owner, LineOf(ticked, chair.Id), true)));
        var all = await Cart(c => c.SelectAllAsync(owner, true));
        Assert.False(all.Items.Single(i => i.VariantId == chair.Id).IsSelected);
        Assert.True(all.Items.Single(i => i.VariantId == set.Id).IsSelected);
        Assert.True(all.AllSelected);                        // every line that can be ordered
    }

    [Fact]
    public async Task Coupon_IsCheckedAgainstTheTickedLines()
    {
        var userId = await AdminServiceTests.CreateCustomerAsync(_host);
        var owner = new CartOwner(userId, null);
        var chair = await VariantAsync("GA-CURVE");                      // below CHAOBAN10's 5.000.000₫ minimum
        var table = await VariantAsync("BA-WALNUT");
        await Cart(c => c.AddAsync(owner, chair.Id, 1));
        var cart = await Cart(c => c.AddAsync(owner, table.Id, 1));
        await Cart(c => c.ApplyCouponAsync(owner, "CHAOBAN10"));

        var withoutTable = await Cart(c => c.SetSelectedAsync(owner, LineOf(cart, table.Id), false));

        Assert.Equal(0, withoutTable.DiscountAmount);
        Assert.Contains("tối thiểu", withoutTable.CouponMessage);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Cart(c => c.ApplyCouponAsync(owner, "CHAOBAN10")));
    }

    [Fact]
    public async Task Lines_OfAnotherCart_CannotBeTicked()
    {
        var (_, _, _, _) = await TwoLinesAsync();
        var otherOwner = new CartOwner(await AdminServiceTests.CreateCustomerAsync(_host), null);
        var chair = await VariantAsync("GA-CURVE");
        await Cart(c => c.AddAsync(otherOwner, chair.Id, 1));
        var foreignLine = await Db(db => db.CartItems.Where(i => i.Cart.UserId != otherOwner.UserId).Select(i => i.Id).FirstAsync());

        await Assert.ThrowsAsync<NotFoundException>(() => Cart(c => c.SetSelectedAsync(otherOwner, foreignLine, false)));
    }
}
