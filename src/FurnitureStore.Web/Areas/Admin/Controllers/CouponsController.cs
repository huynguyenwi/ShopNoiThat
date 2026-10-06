using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Sales;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Areas.Admin.Controllers;

public sealed record CouponFormViewModel(int? Id, CouponCommand Command, CouponDetailDto? Detail);

/// <summary>/admin/coupons - discount codes used in the cart and at checkout.</summary>
[Route("admin/coupons")]
public sealed class CouponsController(ICouponAdminService coupons) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] CouponQuery query, CancellationToken cancellationToken)
    {
        ViewData["Query"] = query;
        return View(await coupons.SearchAsync(query, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken) =>
        View(await coupons.GetDetailAsync(id, cancellationToken));

    [HttpGet("create")]
    public IActionResult Create() => View("Form", new CouponFormViewModel(null, new CouponCommand { IsActive = true, IsPublic = true }, null));

    [HttpPost("create")]
    public async Task<IActionResult> Create([Bind(Prefix = "Command")] CouponCommand command, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            var id = await coupons.CreateAsync(command, cancellationToken);
            SetStatus($"Đã tạo mã giảm giá {command.Code}.");
            return Redirect($"/admin/coupons/{id}");
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex);
            return View("Form", new CouponFormViewModel(null, command, null));
        }
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken) =>
        View("Form", new CouponFormViewModel(id, await coupons.GetForEditAsync(id, cancellationToken), await coupons.GetDetailAsync(id, cancellationToken)));

    [HttpPost("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Command")] CouponCommand command, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await coupons.UpdateAsync(id, command, cancellationToken);
            SetStatus($"Đã lưu mã giảm giá {command.Code}.");
            return Redirect($"/admin/coupons/{id}");
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex);
            return View("Form", new CouponFormViewModel(id, command, await coupons.GetDetailAsync(id, cancellationToken)));
        }
    }

    [HttpPost("{id:int}/active")]
    public async Task<IActionResult> Active(int id, bool active, string? returnUrl, CancellationToken cancellationToken)
    {
        await coupons.SetActiveAsync(id, active, cancellationToken);
        SetStatus(active ? "Đã bật mã giảm giá." : "Đã tắt mã giảm giá. Khách không thể áp dụng mã này nữa.");
        return Redirect(returnUrl is not null && Url.IsLocalUrl(returnUrl) && returnUrl.StartsWith("/admin/coupons", StringComparison.Ordinal)
            ? returnUrl
            : "/admin/coupons");
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            await coupons.DeleteAsync(id, cancellationToken);
            SetStatus("Đã xóa mã giảm giá.");
            return Redirect("/admin/coupons");
        }
        catch (BusinessRuleException ex)
        {
            SetError(ex.Message);
            return Redirect($"/admin/coupons/{id}");
        }
    }
}
