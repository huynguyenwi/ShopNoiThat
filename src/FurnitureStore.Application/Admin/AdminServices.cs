using FurnitureStore.Application.Common.Emails;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Common.Utilities;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Application.Admin;

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class DashboardService(IAdminReportRepository reports, TimeProvider timeProvider) : IDashboardService
{
    /// <summary>Orders counted as revenue: confirmed by the shop and not cancelled / refunded.</summary>
    public static readonly OrderStatus[] RevenueStatuses = [OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipping, OrderStatus.Delivered];

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var today = VietnamTime.Today(nowUtc);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var chartStartMonth = monthStart.AddMonths(-11);
        var facts = await reports.GetOrderFactsAsync(VietnamTime.StartOfDayUtc(chartStartMonth), cancellationToken);

        var localFacts = facts.Select(f => (Date: DateOnly.FromDateTime(VietnamTime.ToLocal(f.PlacedAt)), f.TotalAmount, f.Status)).ToList();
        var revenueFacts = localFacts.Where(f => RevenueStatuses.Contains(f.Status)).ToList();

        var last30 = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29)).ToList();
        var revenueByDay = last30.Select(d => new ChartPointDto(d.ToString("dd/MM"), revenueFacts.Where(f => f.Date == d).Sum(f => f.TotalAmount))).ToList();
        var ordersByDay = last30.Select(d => new ChartPointDto(d.ToString("dd/MM"), localFacts.Count(f => f.Date == d))).ToList();
        var revenueByMonth = Enumerable.Range(0, 12).Select(i => chartStartMonth.AddMonths(i))
            .Select(m => new ChartPointDto(m.ToString("MM/yyyy"),
                revenueFacts.Where(f => f.Date.Year == m.Year && f.Date.Month == m.Month).Sum(f => f.TotalAmount)))
            .ToList();

        var statusCounts = await reports.GetOrderStatusCountsAsync(cancellationToken);
        var customers = await reports.CountCustomersAsync(VietnamTime.StartOfDayUtc(monthStart), cancellationToken);
        var products = await reports.CountProductsAsync(cancellationToken);
        var lowStock = await reports.GetLowStockAsync(10, cancellationToken);
        var recent = await reports.SearchOrdersAsync(new AdminOrderQuery { PageSize = 8 }, null, null, cancellationToken);

        return new DashboardDto(
            TotalRevenue: await reports.GetTotalRevenueAsync(RevenueStatuses, cancellationToken),
            TodayRevenue: revenueFacts.Where(f => f.Date == today).Sum(f => f.TotalAmount),
            MonthRevenue: revenueFacts.Where(f => f.Date >= monthStart).Sum(f => f.TotalAmount),
            TotalOrders: statusCounts.Sum(s => s.Count),
            TodayOrders: localFacts.Count(f => f.Date == today),
            PendingOrders: statusCounts.Where(s => s.Status == OrderStatus.Pending).Sum(s => s.Count),
            TotalCustomers: customers.Total,
            NewCustomersThisMonth: customers.NewSince,
            TotalProducts: products.Total,
            ActiveProducts: products.Active,
            LowStockCount: lowStock.Count,
            LowStock: lowStock.Items,
            RevenueByDay: revenueByDay,
            OrdersByDay: ordersByDay,
            RevenueByMonth: revenueByMonth,
            OrdersByStatus: statusCounts,
            TopProducts: await reports.GetTopProductsAsync(RevenueStatuses, VietnamTime.StartOfDayUtc(today.AddDays(-89)), 10, cancellationToken),
            RecentOrders: recent.Items);
    }
}

public interface IOrderAdminService
{
    Task<PagedResult<AdminOrderListItemDto>> ListAsync(AdminOrderQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StatusCountDto>> GetStatusCountsAsync(CancellationToken cancellationToken = default);
    Task<OrderDetailDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task ChangeStatusAsync(int id, OrderStatus newStatus, string? note, Guid? expectedVersion, CancellationToken cancellationToken = default);
    Task ConfirmPaymentAsync(int id, string? reference, CancellationToken cancellationToken = default);
    Task UpdateAdminNoteAsync(int id, string? note, CancellationToken cancellationToken = default);
    Task UpdateShippingFeeAsync(int id, decimal fee, Guid? expectedVersion, CancellationToken cancellationToken = default);
}

public sealed class OrderAdminService(
    IAdminReportRepository reports,
    IOrderRepository orders,
    OrderWorkflow workflow,
    IPaymentService payments,
    INotificationService notifications,
    IUnitOfWork unitOfWork,
    IEmailSender emailSender,
    IAuditLogService auditLog,
    ICurrentUserService currentUser,
    IOptions<ApplicationSettings> siteOptions,
    TimeProvider timeProvider,
    ILogger<OrderAdminService> logger) : IOrderAdminService
{
    public Task<PagedResult<AdminOrderListItemDto>> ListAsync(AdminOrderQuery query, CancellationToken cancellationToken = default)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 5, 100);
        query.Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var fromUtc = query.From.HasValue ? VietnamTime.StartOfDayUtc(query.From.Value) : (DateTime?)null;
        var toUtc = query.To.HasValue ? VietnamTime.StartOfDayUtc(query.To.Value.AddDays(1)) : (DateTime?)null;
        return reports.SearchOrdersAsync(query, fromUtc, toUtc, cancellationToken);
    }

    public Task<IReadOnlyList<StatusCountDto>> GetStatusCountsAsync(CancellationToken cancellationToken = default) =>
        reports.GetOrderStatusCountsAsync(cancellationToken);

    public async Task<OrderDetailDto> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetFullAsync(id, cancellationToken) ?? throw new NotFoundException("đơn hàng", id);
        return OrderMapper.ToDetail(order, payments.GetInstructions(order));
    }

    public async Task ChangeStatusAsync(int id, OrderStatus newStatus, string? note, Guid? expectedVersion, CancellationToken cancellationToken = default)
    {
        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)];

        var order = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var existing = await orders.GetFullAsync(id, ct) ?? throw new NotFoundException("đơn hàng", id);
            if (expectedVersion.HasValue && expectedVersion.Value != existing.Version)
            {
                throw new ConflictException("Đơn hàng vừa được cập nhật bởi người khác. Vui lòng tải lại trang.");
            }

            if (newStatus == OrderStatus.Cancelled && string.IsNullOrEmpty(cleanNote))
            {
                throw new AppValidationException("Vui lòng nhập lý do hủy đơn.");
            }

            await workflow.ChangeStatusAsync(existing, newStatus, currentUser.UserName, cleanNote, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return existing;
        }, cancellationToken);

        var title = $"Đơn hàng {order.OrderCode}: {OrderStatusTransitions.DisplayName(newStatus)}";
        await notifications.NotifyUserAsync(order.UserId, NotificationType.OrderStatusChanged, title,
            cleanNote ?? "Trạng thái đơn hàng của bạn đã được cập nhật.", $"/account/orders/{order.OrderCode}", cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.StatusChange, nameof(Order), id.ToString(), $"{order.OrderCode} → {newStatus}",
            NewValues: new { Status = newStatus, Note = cleanNote }), cancellationToken);
        logger.LogInformation("Order {OrderCode} moved to {Status} by {User}", order.OrderCode, newStatus, currentUser.UserName);

        await TryEmailAsync(order, title,
            $"""<p>Xin chào <strong>{System.Net.WebUtility.HtmlEncode(order.CustomerName)}</strong>,</p><p>Đơn hàng <strong>{order.OrderCode}</strong> của bạn đã chuyển sang trạng thái <strong>{OrderStatusTransitions.DisplayName(newStatus)}</strong>.</p>{(cleanNote is null ? "" : $"<p>Ghi chú: {System.Net.WebUtility.HtmlEncode(cleanNote)}</p>")}""");
    }

    public async Task ConfirmPaymentAsync(int id, string? reference, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetFullAsync(id, cancellationToken) ?? throw new NotFoundException("đơn hàng", id);
        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            throw new BusinessRuleException("Đơn hàng đã được thanh toán.");
        }

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
        {
            throw new BusinessRuleException("Không thể xác nhận thanh toán cho đơn đã hủy / hoàn tiền.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var payment = order.Payments.OrderByDescending(p => p.Id).FirstOrDefault();
        if (payment is null)
        {
            payment = new Payment { Method = order.PaymentMethod, Amount = order.TotalAmount, Provider = order.PaymentMethod.ToString() };
            order.Payments.Add(payment);
        }

        payment.Status = PaymentStatus.Paid;
        payment.PaidAt = now;
        payment.ProviderResponseCode = "MANUAL";
        if (!string.IsNullOrWhiteSpace(reference))
        {
            payment.TransactionCode = reference.Trim()[..Math.Min(reference.Trim().Length, 100)];
        }
        payment.Note = $"Xác nhận thủ công bởi {currentUser.UserName}";
        order.PaymentStatus = PaymentStatus.Paid;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await notifications.NotifyUserAsync(order.UserId, NotificationType.OrderStatusChanged, $"Đã nhận thanh toán đơn {order.OrderCode}",
            "Cửa hàng đã nhận được tiền thanh toán của bạn.", $"/account/orders/{order.OrderCode}", cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(Payment), payment.Id.ToString(), $"Xác nhận thanh toán đơn {order.OrderCode}",
            NewValues: new { payment.TransactionCode, payment.Amount }), cancellationToken);
        logger.LogInformation("Payment of order {OrderCode} confirmed by {User}", order.OrderCode, currentUser.UserName);
    }

    public async Task UpdateAdminNoteAsync(int id, string? note, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("đơn hàng", id);
        order.AdminNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 1000)];
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Records the delivery &amp; installation fee quoted to the customer by phone; the order total follows.</summary>
    public async Task UpdateShippingFeeAsync(int id, decimal fee, Guid? expectedVersion, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetFullAsync(id, cancellationToken) ?? throw new NotFoundException("đơn hàng", id);
        if (expectedVersion.HasValue && expectedVersion.Value != order.Version)
        {
            throw new ConflictException("Đơn hàng vừa được cập nhật bởi người khác. Vui lòng tải lại trang.");
        }

        var oldFee = order.ShippingFee;
        order.ChangeShippingFee(fee);
        // The unpaid payment record carries the amount to collect.
        foreach (var payment in order.Payments.Where(p => p.Status != PaymentStatus.Paid))
        {
            payment.Amount = order.TotalAmount;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var feeText = order.ShippingFee == 0 ? "miễn phí" : EmailTemplates.Money(order.ShippingFee);
        var title = $"Đơn hàng {order.OrderCode}: đã báo phí giao hàng";
        await notifications.NotifyUserAsync(order.UserId, NotificationType.OrderStatusChanged, title,
            $"Phí giao hàng & lắp đặt: {feeText}. Tổng đơn: {EmailTemplates.Money(order.TotalAmount)}.", $"/account/orders/{order.OrderCode}", cancellationToken);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, nameof(Order), id.ToString(), $"Phí giao hàng đơn {order.OrderCode}",
            OldValues: new { ShippingFee = oldFee }, NewValues: new { order.ShippingFee, order.TotalAmount }), cancellationToken);
        logger.LogInformation("Shipping fee of order {OrderCode} set to {Fee} by {User}", order.OrderCode, order.ShippingFee, currentUser.UserName);

        await TryEmailAsync(order, title,
            $"""<p>Xin chào <strong>{System.Net.WebUtility.HtmlEncode(order.CustomerName)}</strong>,</p><p>Cửa hàng đã cập nhật phí giao hàng &amp; lắp đặt cho đơn <strong>{order.OrderCode}</strong>: <strong>{feeText}</strong>.</p><p>Tổng giá trị đơn hàng: <strong>{EmailTemplates.Money(order.TotalAmount)}</strong>.</p>""");
    }

    private async Task TryEmailAsync(Order order, string subject, string body)
    {
        try
        {
            var site = siteOptions.Value;
            await emailSender.SendAsync(new EmailMessage(order.CustomerEmail, subject, EmailTemplates.Layout(site.SiteName, body), order.CustomerName));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to email status change of order {OrderCode}", order.OrderCode);
        }
    }
}

public interface IAdminActivityService
{
    Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(int take = 20, CancellationToken cancellationToken = default);
    Task<int> CountUnreadNotificationsAsync(CancellationToken cancellationToken = default);
    Task MarkNotificationsReadAsync(CancellationToken cancellationToken = default);
}

public sealed class AdminActivityService(IAdminReportRepository reports, TimeProvider timeProvider) : IAdminActivityService
{
    public Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        query.Page = Math.Max(1, query.Page);
        query.PageSize = Math.Clamp(query.PageSize, 10, 100);
        var fromUtc = query.From.HasValue ? VietnamTime.StartOfDayUtc(query.From.Value) : (DateTime?)null;
        var toUtc = query.To.HasValue ? VietnamTime.StartOfDayUtc(query.To.Value.AddDays(1)) : (DateTime?)null;
        return reports.SearchAuditLogsAsync(query, fromUtc, toUtc, cancellationToken);
    }

    public Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(int take = 20, CancellationToken cancellationToken = default) =>
        reports.GetRoleNotificationsAsync(AppRoles.Admin, Math.Clamp(take, 1, 100), cancellationToken);

    public Task<int> CountUnreadNotificationsAsync(CancellationToken cancellationToken = default) =>
        reports.CountUnreadRoleNotificationsAsync(AppRoles.Admin, cancellationToken);

    public Task MarkNotificationsReadAsync(CancellationToken cancellationToken = default) =>
        reports.MarkRoleNotificationsReadAsync(AppRoles.Admin, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
}
