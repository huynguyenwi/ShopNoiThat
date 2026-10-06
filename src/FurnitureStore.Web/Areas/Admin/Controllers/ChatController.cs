using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Areas.Admin.Controllers;

/// <summary>
/// /admin/chat - answering customer chats. Open to ADMIN and STAFF (BackOffice policy), so it does not inherit the
/// ADMIN-only <see cref="AdminControllerBase"/>. Data is loaded by admin-chat.js from /api/admin/chat and SignalR.
/// </summary>
[Area("Admin")]
[Authorize(Policy = AuthorizationPolicies.BackOffice)]
[Route("admin/chat")]
public sealed class ChatController : Controller
{
    [HttpGet("")]
    public IActionResult Index(int? id) => View(id);
}
