using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Sales;

/// <summary>Admin coupon management (/admin/coupons) and the public offers shown in the cart / checkout.</summary>
public sealed class CouponServiceTests : IAsyncLifetime
{
    // Test clock: 2026-09-01 09:00 UTC = 16:00 in Vietnam.
    private ServiceTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> action) => _host.RunAsync(action);
    private Task<T> Admin<T>(Func<ICouponAdminService, Task<T>> action) => Run(sp => action(sp.GetRequiredService<ICouponAdminService>()));
    private Task Admin(Func<ICouponAdminService, Task> action) => Run(async sp => { await action(sp.GetRequiredService<ICouponAdminService>()); return 0; });
    private Task<T> Cart<T>(Func<ICartService, Task<T>> action) => Run(sp => action(sp.GetRequiredService<ICartService>()));
    private Task<T> Orders<T>(Func<IOrderService, Task<T>> action) => Run(sp => action(sp.GetRequiredService<IOrderService>()));
    private Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action) => Run(sp => action(sp.GetRequiredService<ApplicationDbContext>()));

    private static CouponCommand Command(string code, Action<CouponCommand>? change = null)
    {
        var command = new CouponCommand
        {
            Code = code,
            Name = "Ưu đãi thử nghiệm",
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 100_000,
            IsActive = true,
            IsPublic = true
        };
        change?.Invoke(command);
        return command;
    }

    private async Task<string> CreateUserAsync()
    {
        var email = $"u{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
            FullName = "Khách Test", SecurityStamp = Guid.NewGuid().ToString(), CreatedAt = DateTime.UtcNow
        };
        await Db(async db => { db.Users.Add(user); await db.SaveChangesAsync(); return 0; });
        return user.Id;
    }

    /// <summary>Puts one in-stock chair (about 1.9 million) in the user's cart.</summary>
    private async Task<CartDto> FillCartAsync(CartOwner owner, int quantity = 1)
    {
        var variantId = await Db(db => db.ProductVariants.Where(v => v.Product.Sku == "GA-CURVE" && v.StockQuantity >= 5).Select(v => v.Id).FirstAsync());
        return await Cart(c => c.AddAsync(owner, variantId, quantity));
    }

    private async Task<OrderPlacedResult> OrderWithCouponAsync(string code)
    {
        var owner = new CartOwner(await CreateUserAsync(), null);
        await FillCartAsync(owner);
        await Cart(c => c.ApplyCouponAsync(owner, code));
        return await Orders(o => o.PlaceOrderAsync(owner.UserId!, new CheckoutCommand
        {
            FullName = "Nguyễn Văn Test", Phone = "0912345678", Email = "test@example.com", AddressLine = "12 Lê Lợi",
            Ward = "Phường Sài Gòn", Province = "TP. Hồ Chí Minh", PaymentMethod = PaymentMethod.COD
        }));
    }

    private Task<int> IdOfAsync(string code) => Db(db => db.Coupons.Where(c => c.Code == code).Select(c => c.Id).SingleAsync());

    // ------------------------------------------------------------------ Create / edit

    [Fact]
    public async Task Create_NormalizesCode_StoresVietnamTimesAsUtc_AndIsAudited()
    {
        var id = await Admin(s => s.CreateAsync(Command("  tet-2027 ", c =>
        {
            c.Name = "  Tết sum vầy ";
            c.DiscountType = DiscountType.Percentage;
            c.DiscountValue = 15;
            c.MaxDiscountAmount = 1_500_000;
            c.MinOrderAmount = 3_000_000;
            c.StartsAt = new DateTime(2026, 9, 2, 0, 0, 0);   // Vietnam midnight
            c.EndsAt = new DateTime(2026, 9, 30, 23, 59, 0);
            c.UsageLimit = 100;
            c.UsageLimitPerUser = 1;
        })));

        var saved = await Db(db => db.Coupons.AsNoTracking().SingleAsync(c => c.Id == id));
        Assert.Equal("TET-2027", saved.Code);
        Assert.Equal("Tết sum vầy", saved.Name);
        Assert.Equal(new DateTime(2026, 9, 1, 17, 0, 0), saved.StartsAt);
        Assert.Equal(new DateTime(2026, 9, 30, 16, 59, 0), saved.EndsAt);
        Assert.Equal(CouponStatus.Scheduled, saved.GetStatus(_host.Clock.GetUtcNow().UtcDateTime));
        Assert.True(await Db(db => db.AuditLogs.AnyAsync(a => a.EntityName == nameof(Coupon) && a.EntityId == id.ToString())));

        // The edit form shows Vietnam times again.
        var edit = await Admin(s => s.GetForEditAsync(id));
        Assert.Equal(new DateTime(2026, 9, 2, 0, 0, 0), edit.StartsAt);
        Assert.Equal(3_000_000, edit.MinOrderAmount);
    }

    [Theory]
    [InlineData("GIAM 10", nameof(CouponCommand.Code))]         // space
    [InlineData("AB", nameof(CouponCommand.Code))]              // too short
    [InlineData("GIẢM10", nameof(CouponCommand.Code))]          // Vietnamese letter
    [InlineData("chaoban10", nameof(CouponCommand.Code))]       // already exists (case-insensitive)
    [InlineData("PCT120", nameof(CouponCommand.DiscountValue))]
    [InlineData("FIXED0", nameof(CouponCommand.DiscountValue))]
    [InlineData("DATES", nameof(CouponCommand.EndsAt))]
    [InlineData("PASTEND", nameof(CouponCommand.EndsAt))]
    [InlineData("PERUSER", nameof(CouponCommand.UsageLimitPerUser))]
    [InlineData("NEGMIN", nameof(CouponCommand.MinOrderAmount))]
    public async Task Create_InvalidCommands_AreRejectedPerField(string code, string field)
    {
        var command = Command(code, c =>
        {
            switch (code)
            {
                case "PCT120": c.DiscountType = DiscountType.Percentage; c.DiscountValue = 120; break;
                case "FIXED0": c.DiscountValue = 0; break;
                case "DATES": c.StartsAt = new DateTime(2026, 10, 5); c.EndsAt = new DateTime(2026, 10, 1); break;
                case "PASTEND": c.EndsAt = new DateTime(2026, 8, 1); break;
                case "PERUSER": c.UsageLimit = 2; c.UsageLimitPerUser = 3; break;
                case "NEGMIN": c.MinOrderAmount = -1; break;
            }
        });

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Admin(s => s.CreateAsync(command)));

        Assert.Contains(field, ex.FieldErrors.Keys);
        Assert.False(await Db(db => db.Coupons.AnyAsync(c => c.Code == code.ToUpperInvariant() && c.Code != "CHAOBAN10")));
    }

    [Fact]
    public async Task FixedAmount_IgnoresPercentageCap()
    {
        var id = await Admin(s => s.CreateAsync(Command("FIXCAP", c => c.MaxDiscountAmount = 50_000)));

        Assert.Null(await Db(db => db.Coupons.Where(c => c.Id == id).Select(c => c.MaxDiscountAmount).SingleAsync()));
    }

    [Fact]
    public async Task UsedCoupon_CodeIsLocked_AndLimitCannotDropBelowUses()
    {
        var id = await Admin(s => s.CreateAsync(Command("DUNGROI", c => c.UsageLimit = 5)));
        await OrderWithCouponAsync("DUNGROI");
        await OrderWithCouponAsync("DUNGROI");

        var rename = await Assert.ThrowsAsync<AppValidationException>(() => Admin(s => s.UpdateAsync(id, Command("DOITEN", c => c.UsageLimit = 5))));
        Assert.Contains(nameof(CouponCommand.Code), rename.FieldErrors.Keys);

        var lower = await Assert.ThrowsAsync<AppValidationException>(() => Admin(s => s.UpdateAsync(id, Command("DUNGROI", c => c.UsageLimit = 1))));
        Assert.Contains("(2)", lower.FieldErrors[nameof(CouponCommand.UsageLimit)].Single());

        // Other terms can still change.
        await Admin(s => s.UpdateAsync(id, Command("DUNGROI", c => { c.Name = "Đổi tên chương trình"; c.UsageLimit = 2; })));
        var coupon = await Db(db => db.Coupons.AsNoTracking().SingleAsync(c => c.Id == id));
        Assert.Equal("Đổi tên chương trình", coupon.Name);
        Assert.Equal(CouponStatus.Exhausted, coupon.GetStatus(_host.Clock.GetUtcNow().UtcDateTime));
    }

    [Fact]
    public async Task UnusedCoupon_Rename_KeepsItAppliedInCarts()
    {
        var id = await Admin(s => s.CreateAsync(Command("TENCU")));
        var owner = new CartOwner(await CreateUserAsync(), null);
        await FillCartAsync(owner);
        await Cart(c => c.ApplyCouponAsync(owner, "TENCU"));

        await Admin(s => s.UpdateAsync(id, Command("TENMOI")));

        var cart = await Cart(c => c.GetAsync(owner));
        Assert.Equal("TENMOI", cart.CouponCode);
        Assert.Equal(100_000, cart.DiscountAmount);
    }

    [Fact]
    public async Task Delete_OnlyUnusedCoupons_AndClearsCarts()
    {
        var usedId = await Admin(s => s.CreateAsync(Command("DAXAI")));
        await OrderWithCouponAsync("DAXAI");
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Admin(s => s.DeleteAsync(usedId)));
        Assert.Contains("tắt mã", ex.Message);

        var unusedId = await Admin(s => s.CreateAsync(Command("CHUADUNG")));
        var owner = new CartOwner(await CreateUserAsync(), null);
        await FillCartAsync(owner);
        await Cart(c => c.ApplyCouponAsync(owner, "CHUADUNG"));

        await Admin(s => s.DeleteAsync(unusedId));

        Assert.False(await Db(db => db.Coupons.AnyAsync(c => c.Id == unusedId)));
        Assert.Null((await Cart(c => c.GetAsync(owner))).CouponCode);
        Assert.True(await Db(db => db.AuditLogs.AnyAsync(a => a.EntityName == nameof(Coupon) && a.Action == AuditAction.Delete)));
    }

    [Fact]
    public async Task SwitchedOff_CannotBeApplied()
    {
        var id = await Admin(s => s.CreateAsync(Command("TAMNGUNG")));
        await Admin(s => s.SetActiveAsync(id, false));

        var owner = new CartOwner(await CreateUserAsync(), null);
        await FillCartAsync(owner);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Cart(c => c.ApplyCouponAsync(owner, "TAMNGUNG")));
        Assert.Contains("không còn hiệu lực", ex.Message);
    }

    // ------------------------------------------------------------------ List & statistics

    [Fact]
    public async Task Search_FiltersByComputedStatus_AndText()
    {
        await Admin(s => s.CreateAsync(Command("SAPTOI", c => c.StartsAt = new DateTime(2026, 9, 10))));
        var offId = await Admin(s => s.CreateAsync(Command("DATAT")));
        await Admin(s => s.SetActiveAsync(offId, false));
        await Admin(s => s.CreateAsync(Command("MOTLUOT", c => c.UsageLimit = 1)));
        await OrderWithCouponAsync("MOTLUOT");

        async Task<string[]> Codes(CouponStatus? status, string? search = null) =>
            (await Admin(s => s.SearchAsync(new CouponQuery { Status = status, Search = search, PageSize = 100 }))).Items.Select(i => i.Code).Order().ToArray();

        Assert.Equal(["CHAOBAN10", "GIAM500K"], await Codes(CouponStatus.Running));
        Assert.Equal(["SAPTOI"], await Codes(CouponStatus.Scheduled));
        Assert.Equal(["HETHAN"], await Codes(CouponStatus.Expired));
        Assert.Equal(["MOTLUOT"], await Codes(CouponStatus.Exhausted));
        Assert.Equal(["DATAT"], await Codes(CouponStatus.Inactive));
        Assert.Equal(["CHAOBAN10"], await Codes(null, "chaoban"));
        Assert.Equal(["GIAM500K"], await Codes(null, "500.000"));

        var all = await Admin(s => s.SearchAsync(new CouponQuery { PageSize = 100 }));
        Assert.All(all.Items, i => Assert.Equal(i.Status, i.Code switch
        {
            "SAPTOI" => CouponStatus.Scheduled, "DATAT" => CouponStatus.Inactive, "MOTLUOT" => CouponStatus.Exhausted,
            "HETHAN" => CouponStatus.Expired, _ => CouponStatus.Running
        }));
    }

    [Fact]
    public async Task Detail_CountsOrdersDiscountAndRevenue_ExcludingCancelledOnes()
    {
        var id = await Admin(s => s.CreateAsync(Command("THONGKE", c => c.UsageLimit = 10)));
        var first = await OrderWithCouponAsync("THONGKE");
        var second = await OrderWithCouponAsync("THONGKE");
        var secondUser = await Db(db => db.Orders.Where(o => o.OrderCode == second.OrderCode).Select(o => o.UserId).SingleAsync());
        await Orders(async o => { await o.CancelMyOrderAsync(secondUser, second.OrderCode, "Đổi ý"); return 0; });

        var detail = await Admin(s => s.GetDetailAsync(id));

        Assert.Equal(1, detail.Coupon.UsedCount);              // released by the cancellation
        Assert.Equal(2, detail.OrderCount);                    // history keeps both orders
        Assert.Equal(100_000, detail.Coupon.TotalDiscount);    // only the valid order
        Assert.Equal(first.Total, detail.Revenue);
        Assert.Equal(new[] { first.OrderCode, second.OrderCode }.Order(), detail.RecentOrders.Select(o => o.OrderCode).Order());
        Assert.True(detail.HasOrders);

        // The confirmation e-mail names the coupon next to the discount.
        Assert.Contains(_host.Emails.Sent, m => m.HtmlBody.Contains(first.OrderCode) && m.HtmlBody.Contains("Giảm giá (mã THONGKE)"));
    }

    // ------------------------------------------------------------------ Offers in the cart / checkout

    [Fact]
    public async Task Offers_ListOnlyPublicRunningCoupons_WithEligibilityForThisCart()
    {
        await Admin(s => s.CreateAsync(Command("RIENGTU", c => c.IsPublic = false)));
        await Admin(s => s.CreateAsync(Command("SAPTOI", c => c.StartsAt = new DateTime(2026, 9, 10))));
        await Admin(s => s.CreateAsync(Command("GIAM5", c => { c.DiscountType = DiscountType.Percentage; c.DiscountValue = 5; })));

        var owner = new CartOwner(await CreateUserAsync(), null);
        var cart = await FillCartAsync(owner); // below 5 million
        var offers = await Cart(c => c.GetOffersAsync(owner, cart));

        Assert.Equal(["GIAM5", "CHAOBAN10", "GIAM500K"], offers.Select(o => o.Code).ToArray()); // usable first
        var small = offers.Single(o => o.Code == "GIAM5");
        Assert.True(small.IsEligible);
        Assert.Equal(Math.Round(cart.Subtotal * 0.05m, 0, MidpointRounding.AwayFromZero), small.EstimatedDiscount);
        Assert.Equal("Giảm 5%", small.Benefit);

        var welcome = offers.Single(o => o.Code == "CHAOBAN10");
        Assert.False(welcome.IsEligible);
        Assert.Equal($"Mua thêm {CouponText.Money(5_000_000 - cart.Subtotal)} để dùng mã này.", welcome.Reason);
        Assert.Contains("Đơn từ 5.000.000₫", welcome.Conditions);

        // Applied one is flagged and listed first.
        var applied = await Cart(c => c.ApplyCouponAsync(owner, "giam5"));
        var afterApply = await Cart(c => c.GetOffersAsync(owner, applied));
        Assert.True(afterApply[0].IsApplied);
        Assert.Equal("GIAM5", afterApply[0].Code);
    }

    [Fact]
    public async Task Offers_PerCustomerLimitReached_IsShownAsUnavailable()
    {
        await Admin(s => s.CreateAsync(Command("MOTLAN", c => c.UsageLimitPerUser = 1)));
        var userId = await CreateUserAsync();
        var owner = new CartOwner(userId, null);
        await FillCartAsync(owner);
        await Cart(c => c.ApplyCouponAsync(owner, "MOTLAN"));
        await Orders(o => o.PlaceOrderAsync(userId, new CheckoutCommand
        {
            FullName = "Nguyễn Văn Test", Phone = "0912345678", Email = "test@example.com", AddressLine = "12 Lê Lợi",
            Ward = "Phường Sài Gòn", Province = "TP. Hồ Chí Minh", PaymentMethod = PaymentMethod.COD
        }));

        var cart = await FillCartAsync(owner);
        var offer = (await Cart(c => c.GetOffersAsync(owner, cart))).Single(o => o.Code == "MOTLAN");

        Assert.False(offer.IsEligible);
        Assert.Contains("hết lượt", offer.Reason);
    }

    [Fact]
    public async Task Offers_EmptyCart_ShowsNothing()
    {
        var owner = new CartOwner(await CreateUserAsync(), null);

        Assert.Empty(await Cart(async c => await c.GetOffersAsync(owner, await c.GetAsync(owner))));
    }
}
