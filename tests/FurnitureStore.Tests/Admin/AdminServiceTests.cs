using FurnitureStore.Application.Admin;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Domain.Exceptions;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Admin;

/// <summary>Order management, dashboard statistics and activity log (Phase 6) on a real SQLite schema.</summary>
public sealed class AdminServiceTests : IAsyncLifetime
{
    private ServiceTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> action) => _host.RunAsync(action);
    private Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action) => Run(sp => action(sp.GetRequiredService<ApplicationDbContext>()));
    private Task Admin(Func<IOrderAdminService, Task> action) => _host.RunAsync(sp => action(sp.GetRequiredService<IOrderAdminService>()));
    private Task<T> Admin<T>(Func<IOrderAdminService, Task<T>> action) => Run(sp => action(sp.GetRequiredService<IOrderAdminService>()));

    internal static async Task<string> CreateCustomerAsync(ServiceTestHost host, string? email = null)
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
            CreatedAt = host.Clock.GetUtcNow().UtcDateTime
        };

        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.Users.Add(user);
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == AppRoles.User);
            if (role is null)
            {
                role = new ApplicationRole { Name = AppRoles.User, NormalizedName = AppRoles.User };
                db.Roles.Add(role);
            }

            db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
            await db.SaveChangesAsync();
        });
        return user.Id;
    }

    /// <summary>Puts <paramref name="quantity"/> units of the product in the user's cart and places a COD / bank-transfer order.</summary>
    internal static async Task<OrderPlacedResult> PlaceOrderAsync(ServiceTestHost host, string userId, string productSku, int quantity = 1,
        PaymentMethod method = PaymentMethod.COD)
    {
        var variant = await host.RunAsync(sp => sp.GetRequiredService<ApplicationDbContext>().ProductVariants.AsNoTracking()
            .FirstAsync(v => v.Product.Sku == productSku && v.StockQuantity >= quantity));
        await host.RunAsync(sp => sp.GetRequiredService<ICartService>().AddAsync(new CartOwner(userId, null), variant.Id, quantity));
        return await host.RunAsync(sp => sp.GetRequiredService<IOrderService>().PlaceOrderAsync(userId, new CheckoutCommand
        {
            FullName = "Nguyễn Văn Test",
            Phone = "0912345678",
            Email = "test@example.com",
            AddressLine = "12 Lê Lợi",
            Ward = "Phường Sài Gòn",
            Province = "TP. Hồ Chí Minh",
            PaymentMethod = method
        }));
    }

    /// <summary>Moves an order through the whole workflow up to Delivered, as an admin would.</summary>
    internal static async Task DeliverAsync(ServiceTestHost host, int orderId)
    {
        foreach (var status in new[] { OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipping, OrderStatus.Delivered })
        {
            await host.RunAsync(sp => sp.GetRequiredService<IOrderAdminService>().ChangeStatusAsync(orderId, status, null, null));
        }
    }

    private Task<int> StockOfAsync(int orderId) =>
        Db(db => db.OrderItems.Where(i => i.OrderId == orderId)
            .Join(db.ProductVariants, i => i.ProductVariantId, v => v.Id, (i, v) => v.StockQuantity).SingleAsync());

    // ------------------------------------------------------------------ Order workflow

    [Fact]
    public async Task ChangeStatus_FullWorkflow_RecordsHistory_MarksCodPaid_NotifiesCustomer()
    {
        var userId = await CreateCustomerAsync(_host);
        var placed = await PlaceOrderAsync(_host, userId, "KTT-PINE");

        await DeliverAsync(_host, placed.OrderId);

        var order = await Admin(a => a.GetAsync(placed.OrderId));
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus); // COD is collected on delivery
        Assert.Equal(5, order.History.Count);                  // Pending + 4 changes
        Assert.Equal(4, await Db(db => db.Notifications.CountAsync(n => n.UserId == userId && n.Type == NotificationType.OrderStatusChanged)));
        Assert.Contains(_host.Emails.Sent, e => e.Subject.Contains(placed.OrderCode) && e.Subject.Contains("Đã giao"));
        Assert.True(await Db(db => db.AuditLogs.AnyAsync(l => l.EntityName == nameof(Order) && l.EntityId == placed.OrderId.ToString() && l.Action == AuditAction.StatusChange)));
    }

    [Fact]
    public async Task ChangeStatus_SkippingSteps_IsRejected()
    {
        var placed = await PlaceOrderAsync(_host, await CreateCustomerAsync(_host), "KTT-PINE");

        await Assert.ThrowsAsync<DomainException>(() => Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Delivered, null, null)));
        await Assert.ThrowsAsync<DomainException>(() => Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Refunded, null, null)));

        Assert.Equal(OrderStatus.Pending, (await Admin(a => a.GetAsync(placed.OrderId))).Status);
    }

    [Fact]
    public async Task Cancel_RequiresReason_ThenRestocks()
    {
        var placed = await PlaceOrderAsync(_host, await CreateCustomerAsync(_host), "BA-WALNUT", 2);
        var stockAfterOrder = await StockOfAsync(placed.OrderId);
        await Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Confirmed, null, null));

        await Assert.ThrowsAsync<AppValidationException>(() => Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Cancelled, "  ", null)));

        await Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Cancelled, "Khách đổi ý", null));
        var order = await Admin(a => a.GetAsync(placed.OrderId));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Khách đổi ý", order.CancelReason);
        Assert.Equal(stockAfterOrder + 2, await StockOfAsync(placed.OrderId));
    }

    // ------------------------------------------------------------------ Delivery fee quoted by phone

    [Fact]
    public async Task ShippingFee_QuotedByPhone_UpdatesTheTotalToCollect_AndTellsTheCustomer()
    {
        var userId = await CreateCustomerAsync(_host);
        var placed = await PlaceOrderAsync(_host, userId, "BBA-ANGIA");
        var before = await Admin(a => a.GetAsync(placed.OrderId));
        Assert.Equal(0, before.ShippingFee);                  // not priced on the website
        Assert.True(before.CanChangeShippingFee);

        await Admin(a => a.UpdateShippingFeeAsync(placed.OrderId, 450_000, before.Version));

        var order = await Admin(a => a.GetAsync(placed.OrderId));
        Assert.Equal(450_000, order.ShippingFee);
        Assert.Equal(before.TotalAmount + 450_000, order.TotalAmount);
        Assert.Equal(order.TotalAmount, Assert.Single(order.Payments).Amount);   // the COD amount to collect
        Assert.True(await Db(db => db.Notifications.AnyAsync(n => n.UserId == userId && n.Title.Contains("đã báo phí giao hàng"))));
        Assert.Contains(_host.Emails.Sent, e => e.Subject.Contains(placed.OrderCode) && e.HtmlBody.Contains("450.000₫"));
        Assert.True(await Db(db => db.AuditLogs.AnyAsync(l => l.EntityName == nameof(Order) && l.EntityId == placed.OrderId.ToString()
            && l.Action == AuditAction.Update && l.NewValues!.Contains("450000"))));
    }

    [Fact]
    public async Task ShippingFee_StaleVersion_OutOfRange_AndDeliveredOrders_AreRejected()
    {
        var placed = await PlaceOrderAsync(_host, await CreateCustomerAsync(_host), "BBA-ANGIA");
        var staleVersion = (await Admin(a => a.GetAsync(placed.OrderId))).Version;
        await Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Confirmed, null, null));

        await Assert.ThrowsAsync<ConflictException>(() => Admin(a => a.UpdateShippingFeeAsync(placed.OrderId, 300_000, staleVersion)));
        await Assert.ThrowsAsync<DomainException>(() => Admin(a => a.UpdateShippingFeeAsync(placed.OrderId, -1, null)));
        await Assert.ThrowsAsync<DomainException>(() => Admin(a => a.UpdateShippingFeeAsync(placed.OrderId, 100_000_001, null)));

        foreach (var status in new[] { OrderStatus.Processing, OrderStatus.Shipping, OrderStatus.Delivered })
        {
            await Admin(a => a.ChangeStatusAsync(placed.OrderId, status, null, null));
        }

        await Assert.ThrowsAsync<DomainException>(() => Admin(a => a.UpdateShippingFeeAsync(placed.OrderId, 300_000, null)));
        var order = await Admin(a => a.GetAsync(placed.OrderId));
        Assert.False(order.CanChangeShippingFee);
        Assert.Equal(0, order.ShippingFee);
    }

    [Fact]
    public async Task Refund_AfterDelivery_RestocksInventory()
    {
        var placed = await PlaceOrderAsync(_host, await CreateCustomerAsync(_host), "KTT-PINE");
        var stockAfterOrder = await StockOfAsync(placed.OrderId);
        await DeliverAsync(_host, placed.OrderId);

        await Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Refunded, "Lỗi bề mặt", null));

        Assert.Equal(stockAfterOrder + 1, await StockOfAsync(placed.OrderId));
        Assert.Equal(OrderStatus.Refunded, (await Admin(a => a.GetAsync(placed.OrderId))).Status);
    }

    [Fact]
    public async Task ChangeStatus_WithStaleVersion_IsAConflict()
    {
        var placed = await PlaceOrderAsync(_host, await CreateCustomerAsync(_host), "KTT-PINE");
        var version = await Db(db => db.Orders.Where(o => o.Id == placed.OrderId).Select(o => o.Version).SingleAsync());

        await Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Confirmed, null, version));

        // A second admin still has the page open with the old version.
        await Assert.ThrowsAsync<ConflictException>(() => Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Cancelled, "Hết hàng", version)));
        Assert.Equal(OrderStatus.Confirmed, (await Admin(a => a.GetAsync(placed.OrderId))).Status);
    }

    [Fact]
    public async Task ConfirmPayment_BankTransfer_MarksPaidOnce()
    {
        var placed = await PlaceOrderAsync(_host, await CreateCustomerAsync(_host), "KTT-PINE", method: PaymentMethod.BankTransfer);

        await Admin(a => a.ConfirmPaymentAsync(placed.OrderId, "FT26092912345"));

        var order = await Admin(a => a.GetAsync(placed.OrderId));
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        var payment = Assert.Single(order.Payments);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal("FT26092912345", payment.TransactionCode);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Admin(a => a.ConfirmPaymentAsync(placed.OrderId, null)));
    }

    [Fact]
    public async Task ConfirmPayment_OnCancelledOrder_IsRejected()
    {
        var placed = await PlaceOrderAsync(_host, await CreateCustomerAsync(_host), "KTT-PINE", method: PaymentMethod.BankTransfer);
        await Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Cancelled, "Khách hủy", null));

        await Assert.ThrowsAsync<BusinessRuleException>(() => Admin(a => a.ConfirmPaymentAsync(placed.OrderId, null)));
    }

    [Fact]
    public async Task AdminNote_IsSavedAndTrimmed()
    {
        var placed = await PlaceOrderAsync(_host, await CreateCustomerAsync(_host), "KTT-PINE");

        await Admin(a => a.UpdateAdminNoteAsync(placed.OrderId, "  Giao giờ hành chính  "));

        Assert.Equal("Giao giờ hành chính", await Db(db => db.Orders.Where(o => o.Id == placed.OrderId).Select(o => o.AdminNote).SingleAsync()));
    }

    [Fact]
    public async Task OrderList_FiltersBySearchStatusAndPaymentMethod()
    {
        var userId = await CreateCustomerAsync(_host);
        var first = await PlaceOrderAsync(_host, userId, "KTT-PINE");
        var second = await PlaceOrderAsync(_host, userId, "KTT-PINE", method: PaymentMethod.BankTransfer);
        await Admin(a => a.ChangeStatusAsync(second.OrderId, OrderStatus.Confirmed, null, null));

        var byCode = await Admin(a => a.ListAsync(new AdminOrderQuery { Search = first.OrderCode.ToLowerInvariant() }));
        Assert.Equal(first.OrderCode, Assert.Single(byCode.Items).OrderCode);

        var confirmed = await Admin(a => a.ListAsync(new AdminOrderQuery { Status = OrderStatus.Confirmed }));
        Assert.Equal(second.OrderCode, Assert.Single(confirmed.Items).OrderCode);

        var counts = await Admin(a => a.GetStatusCountsAsync());
        Assert.Equal(1, counts.Single(c => c.Status == OrderStatus.Pending).Count);
        Assert.Equal(1, counts.Single(c => c.Status == OrderStatus.Confirmed).Count);
    }

    // ------------------------------------------------------------------ Dashboard

    [Fact]
    public async Task Dashboard_CountsOnlyRealisedRevenue_AndReportsStockAndTopProducts()
    {
        var userId = await CreateCustomerAsync(_host);
        var delivered = await PlaceOrderAsync(_host, userId, "BA-WALNUT");
        await DeliverAsync(_host, delivered.OrderId);
        var pending = await PlaceOrderAsync(_host, userId, "KTT-PINE");
        var cancelled = await PlaceOrderAsync(_host, userId, "KTT-PINE");
        await Admin(a => a.ChangeStatusAsync(cancelled.OrderId, OrderStatus.Cancelled, "Test", null));

        var dashboard = await Run(sp => sp.GetRequiredService<IDashboardService>().GetAsync());

        Assert.Equal(delivered.Total, dashboard.TotalRevenue);          // pending & cancelled are not revenue
        Assert.Equal(delivered.Total, dashboard.TodayRevenue);
        Assert.Equal(delivered.Total, dashboard.MonthRevenue);
        Assert.Equal(3, dashboard.TotalOrders);
        Assert.Equal(3, dashboard.TodayOrders);
        Assert.Equal(1, dashboard.PendingOrders);
        Assert.Equal(1, dashboard.TotalCustomers);
        Assert.Equal(1, dashboard.NewCustomersThisMonth);
        Assert.True(dashboard.TotalProducts >= 30);
        Assert.True(dashboard.LowStockCount > 0);                       // the demo catalog has sold-out variants
        Assert.Equal(30, dashboard.RevenueByDay.Count);
        Assert.Equal(12, dashboard.RevenueByMonth.Count);
        Assert.Equal(delivered.Total, dashboard.RevenueByDay[^1].Value);
        Assert.Equal(3, dashboard.OrdersByDay[^1].Value);
        var walnutId = await Db(db => db.Products.Where(p => p.Sku == "BA-WALNUT").Select(p => p.Id).SingleAsync());
        Assert.Equal(walnutId, Assert.Single(dashboard.TopProducts).ProductId);
        Assert.Equal(3, dashboard.RecentOrders.Count);
        Assert.Equal(pending.OrderCode, dashboard.RecentOrders.Single(o => o.Status == OrderStatus.Pending).OrderCode);
    }

    [Fact]
    public async Task Dashboard_GroupsByVietnamDay()
    {
        // 18:30 UTC on Sept 1 is already Sept 2 (01:30) in Vietnam.
        _host.Clock.Advance(TimeSpan.FromHours(9.5));
        var placed = await PlaceOrderAsync(_host, await CreateCustomerAsync(_host), "KTT-PINE");
        await Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Confirmed, null, null));

        var dashboard = await Run(sp => sp.GetRequiredService<IDashboardService>().GetAsync());

        Assert.Equal("02/09", dashboard.RevenueByDay[^1].Label);
        Assert.Equal(placed.Total, dashboard.RevenueByDay[^1].Value);
        Assert.Equal(placed.Total, dashboard.TodayRevenue);
    }

    // ------------------------------------------------------------------ Activity

    [Fact]
    public async Task AdminNotifications_CountAndMarkRead()
    {
        await PlaceOrderAsync(_host, await CreateCustomerAsync(_host), "KTT-PINE");
        var activity = (Func<IAdminActivityService, Task<int>> f) => Run(sp => f(sp.GetRequiredService<IAdminActivityService>()));

        Assert.Equal(1, await activity(a => a.CountUnreadNotificationsAsync()));
        var list = await Run(sp => sp.GetRequiredService<IAdminActivityService>().GetNotificationsAsync());
        Assert.Contains(list, n => n.Type == NotificationType.OrderPlaced && !n.IsRead);

        await activity(async a => { await a.MarkNotificationsReadAsync(); return 0; });
        Assert.Equal(0, await activity(a => a.CountUnreadNotificationsAsync()));
    }

    [Fact]
    public async Task AuditLogs_AreSearchableByOrderCode()
    {
        var placed = await PlaceOrderAsync(_host, await CreateCustomerAsync(_host), "KTT-PINE");
        await Admin(a => a.ChangeStatusAsync(placed.OrderId, OrderStatus.Confirmed, "password=abc", null));

        var logs = await Run(sp => sp.GetRequiredService<IAdminActivityService>().GetAuditLogsAsync(new AuditLogQuery { Search = placed.OrderCode }));

        Assert.Equal(2, logs.TotalCount); // "Đặt hàng ..." by the customer + the status change
        Assert.All(logs.Items, l => Assert.Equal(nameof(Order), l.EntityName));

        var changes = await Run(sp => sp.GetRequiredService<IAdminActivityService>().GetAuditLogsAsync(
            new AuditLogQuery { Search = placed.OrderCode, Action = AuditAction.StatusChange }));
        var log = Assert.Single(changes.Items);
        Assert.Contains("\"Status\":\"Confirmed\"", log.NewValues);
    }
}
