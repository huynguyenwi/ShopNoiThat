using FurnitureStore.Application.Admin;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Areas.Admin.Controllers;

public sealed class DashboardController(IDashboardService dashboard) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await dashboard.GetAsync(cancellationToken));
}
