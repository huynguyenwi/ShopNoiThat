using FurnitureStore.Application.Admin;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Web.Areas.Admin.Models;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Areas.Admin.Controllers;

/// <summary>/admin/orders - order list, detail, status workflow, payment confirmation.</summary>
public sealed class OrdersController(IOrderAdminService orders, IStoreInfoService storeInfo) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminOrderQuery query, CancellationToken cancellationToken) =>
        View(new AdminOrderListViewModel
        {
            Query = query,
            Result = await orders.ListAsync(query, cancellationToken),
            StatusCounts = await orders.GetStatusCountsAsync(cancellationToken)
        });

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken) =>
        View(await orders.GetAsync(id, cancellationToken));

    /// <summary>Printable delivery slip with the order QR code (staff scan it to open this order).</summary>
    [HttpGet]
    public async Task<IActionResult> Print(int id, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(id, cancellationToken);
        var store = await storeInfo.GetAsync(cancellationToken);
        return View(new OrderSlipViewModel(order, store.Name, store.Address, store.Hotline));
    }

    [HttpPost]
    public async Task<IActionResult> Status(int id, OrderStatus status, string? note, Guid? version, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await orders.ChangeStatusAsync(id, status, note, version, cancellationToken);
            SetStatus($"Đã chuyển đơn hàng sang \"{Infrastructure.Labels.Of(status)}\".");
        }
        catch (Exception ex) when (ex is Domain.Exceptions.DomainException or AppValidationException or ConflictException or BusinessRuleException)
        {
            SetError(ex is AppValidationException validation ? string.Join(" ", validation.Errors) : ex.Message);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ConfirmPayment(int id, string? reference, CancellationToken cancellationToken)
    {
        try
        {
            await orders.ConfirmPaymentAsync(id, reference, cancellationToken);
            SetStatus("Đã xác nhận thanh toán.");
        }
        catch (BusinessRuleException ex)
        {
            SetError(ex.Message);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Delivery &amp; installation fee quoted to the customer by phone.</summary>
    [HttpPost]
    public async Task<IActionResult> ShippingFee(int id, decimal shippingFee, Guid? version, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await orders.UpdateShippingFeeAsync(id, shippingFee, version, cancellationToken);
            SetStatus("Đã lưu phí giao hàng & lắp đặt, tổng đơn đã được cập nhật.");
        }
        catch (Exception ex) when (ex is Domain.Exceptions.DomainException or AppValidationException or ConflictException)
        {
            SetError(ex is AppValidationException validation ? string.Join(" ", validation.Errors) : ex.Message);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Note(int id, string? adminNote, CancellationToken cancellationToken)
    {
        await orders.UpdateAdminNoteAsync(id, adminNote, cancellationToken);
        SetStatus("Đã lưu ghi chú nội bộ.");
        return RedirectToAction(nameof(Details), new { id });
    }
}

/// <summary>/admin/customers - accounts, lock / unlock, roles.</summary>
public sealed class CustomersController(IUserAdminService users) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminUserQuery query, CancellationToken cancellationToken) =>
        View(new AdminUserListViewModel { Query = query, Result = await users.ListAsync(query, cancellationToken) });

    [HttpGet]
    public async Task<IActionResult> Details(string id, CancellationToken cancellationToken) =>
        View(await users.GetAsync(id, cancellationToken));

    [HttpPost]
    public Task<IActionResult> Lock(string id, string? reason, CancellationToken cancellationToken) =>
        Run(id, () => users.LockAsync(id, reason, cancellationToken), "Đã khóa tài khoản. Người dùng sẽ bị đăng xuất trong vòng 1 phút.");

    [HttpPost]
    public Task<IActionResult> Unlock(string id, CancellationToken cancellationToken) =>
        Run(id, () => users.UnlockAsync(id, cancellationToken), "Đã mở khóa tài khoản.");

    [HttpPost]
    public Task<IActionResult> Role(string id, string role, bool enabled, CancellationToken cancellationToken) =>
        Run(id, () => users.SetRoleAsync(id, role, enabled, cancellationToken), "Đã cập nhật vai trò.");

    private async Task<IActionResult> Run(string id, Func<Task> action, string success)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await action();
            SetStatus(success);
        }
        catch (Exception ex) when (ex is BusinessRuleException or AppValidationException)
        {
            SetError(ex is AppValidationException validation ? string.Join(" ", validation.Errors) : ex.Message);
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}

/// <summary>/admin/reviews - moderation.</summary>
public sealed class ReviewsController(IReviewService reviews) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminReviewQuery query, CancellationToken cancellationToken) =>
        View(new AdminReviewListViewModel { Query = query, Result = await reviews.SearchAsync(query, cancellationToken) });

    [HttpPost]
    public async Task<IActionResult> Visibility(int id, bool hidden, string? returnUrl, CancellationToken cancellationToken)
    {
        await reviews.SetHiddenAsync(id, hidden, cancellationToken);
        SetStatus(hidden ? "Đã ẩn đánh giá." : "Đã hiển thị lại đánh giá.");
        return BackTo(returnUrl);
    }

    [HttpPost]
    public async Task<IActionResult> Reply(int id, string? reply, string? returnUrl, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await reviews.ReplyAsync(id, reply, cancellationToken);
            SetStatus("Đã lưu phản hồi.");
        }
        catch (AppValidationException ex)
        {
            SetError(string.Join(" ", ex.Errors));
        }

        return BackTo(returnUrl);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, string? returnUrl, CancellationToken cancellationToken)
    {
        await reviews.DeleteAsync(id, cancellationToken);
        SetStatus("Đã xóa đánh giá.");
        return BackTo(returnUrl);
    }

    private IActionResult BackTo(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
}

/// <summary>/admin/contacts - messages from the contact form.</summary>
public sealed class ContactsController(IContactService contacts) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(ContactMessageStatus? status, string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        ViewData["Status"] = status;
        ViewData["Search"] = search;
        return View(await contacts.ListAsync(status, search, page, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken) =>
        View(await contacts.OpenAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Update(int id, ContactMessageStatus status, string? adminNote, CancellationToken cancellationToken)
    {
        await contacts.UpdateAsync(id, status, adminNote, cancellationToken);
        SetStatus("Đã cập nhật liên hệ.");
        return RedirectToAction(nameof(Details), new { id });
    }
}

/// <summary>/admin/store - store / workshop information shown across the site.</summary>
public sealed class StoreController(IStoreInfoService store) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var info = await store.GetAsync(cancellationToken);
        return View(new StoreInfoCommand
        {
            Name = info.Name, Tagline = info.Tagline, About = info.About, Address = info.Address, WorkshopAddress = info.WorkshopAddress,
            Hotline = info.Hotline, Email = info.Email, OpeningHours = info.OpeningHours, FacebookUrl = info.FacebookUrl,
            TikTokUrl = info.TikTokUrl, ZaloUrl = info.ZaloUrl, GoogleMapsEmbedUrl = info.GoogleMapsEmbedUrl
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(StoreInfoCommand command, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await store.UpdateAsync(command, cancellationToken);
            SetStatus("Đã lưu thông tin cửa hàng.");
            return RedirectToAction(nameof(Index));
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex, prefix: string.Empty);
            return View(command);
        }
    }
}

/// <summary>/admin/auditlogs and /admin/notifications.</summary>
public sealed class ActivityController(IAdminActivityService activity) : AdminControllerBase
{
    [HttpGet("admin/audit-logs")]
    public async Task<IActionResult> AuditLogs([FromQuery] AuditLogQuery query, CancellationToken cancellationToken)
    {
        ViewData["Query"] = query;
        return View(await activity.GetAuditLogsAsync(query, cancellationToken));
    }

    [HttpGet("admin/notifications")]
    public async Task<IActionResult> Notifications(CancellationToken cancellationToken) =>
        View(await activity.GetNotificationsAsync(100, cancellationToken));

    [HttpPost("admin/notifications/read")]
    public async Task<IActionResult> MarkRead(CancellationToken cancellationToken)
    {
        await activity.MarkNotificationsReadAsync(cancellationToken);
        return Redirect("/admin/notifications");
    }
}
