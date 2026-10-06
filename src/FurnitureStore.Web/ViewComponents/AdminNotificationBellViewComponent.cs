using FurnitureStore.Application.Admin;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.ViewComponents;

/// <summary>Bell icon with the number of unread admin notifications (new orders, contacts, reviews...).</summary>
public sealed class AdminNotificationBellViewComponent(IAdminActivityService activity) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() =>
        View(await activity.CountUnreadNotificationsAsync(HttpContext.RequestAborted));
}
