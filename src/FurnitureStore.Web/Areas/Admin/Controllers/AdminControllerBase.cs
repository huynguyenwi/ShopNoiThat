using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Areas.Admin.Controllers;

/// <summary>
/// Every controller of the Admin area inherits this: the ADMIN role is enforced on the server for every
/// action, independently of what the UI shows or hides.
/// </summary>
[Area("Admin")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public abstract class AdminControllerBase : Controller
{
    public const string StatusMessageKey = "AdminStatusMessage";

    protected void SetStatus(string message) => TempData[StatusMessageKey] = message;

    protected void SetError(string message) => TempData[StatusMessageKey] = "Lỗi: " + message;
}
