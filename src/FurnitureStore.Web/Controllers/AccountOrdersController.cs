using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Sales;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.ViewModels.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers;

/// <summary>/account/orders, /account/addresses and /wishlist - the customer's own data only.</summary>
[Authorize(Policy = AuthorizationPolicies.SignedIn)]
public sealed class AccountOrdersController(IOrderService orders, IAddressService addresses, IWishlistService wishlist) : Controller
{
    private string UserId => User.UserId()!;

    [HttpGet("account/orders")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        ViewData["Robots"] = "noindex,nofollow";
        return View(await orders.GetMyOrdersAsync(UserId, page, 10, cancellationToken));
    }

    [HttpGet("account/orders/{code}")]
    public async Task<IActionResult> Detail(string code, CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        return View(await orders.GetMyOrderAsync(UserId, code, cancellationToken));
    }

    [HttpPost("account/orders/{code}/cancel")]
    public async Task<IActionResult> Cancel(string code, string? reason, CancellationToken cancellationToken)
    {
        try
        {
            await orders.CancelMyOrderAsync(UserId, code, reason, cancellationToken);
            TempData[AccountController.StatusMessageKey] = $"Đã hủy đơn hàng {code}.";
        }
        catch (BusinessRuleException ex)
        {
            TempData[AccountController.StatusMessageKey] = "Lỗi: " + ex.Message;
        }

        return Redirect($"/account/orders/{Uri.EscapeDataString(code.ToUpperInvariant())}");
    }

    // ------------------------------------------------------------------ Addresses

    [HttpGet("account/addresses")]
    public async Task<IActionResult> Addresses(int? edit, CancellationToken cancellationToken)
    {
        var list = await addresses.ListAsync(UserId, cancellationToken);
        var command = new CustomerAddressCommand();
        if (edit.HasValue)
        {
            var current = await addresses.GetAsync(UserId, edit.Value, cancellationToken);
            command = new CustomerAddressCommand
            {
                Label = current.Label, RecipientName = current.RecipientName, Phone = current.Phone, AddressLine = current.AddressLine,
                Ward = current.Ward, Province = current.Province, IsDefault = current.IsDefault
            };
        }

        ViewData["Robots"] = "noindex,nofollow";
        return View(new AddressPageViewModel { Addresses = list, Command = command, EditingId = edit });
    }

    [HttpPost("account/addresses")]
    public async Task<IActionResult> SaveAddress(int? id, [Bind(Prefix = "Command")] CustomerAddressCommand command, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            if (id.HasValue)
            {
                await addresses.UpdateAsync(UserId, id.Value, command, cancellationToken);
            }
            else
            {
                await addresses.CreateAsync(UserId, command, cancellationToken);
            }

            TempData[AccountController.StatusMessageKey] = "Đã lưu địa chỉ.";
            return RedirectToAction(nameof(Addresses));
        }
        catch (AppValidationException ex)
        {
            foreach (var (field, messages) in ex.FieldErrors)
            {
                foreach (var message in messages) ModelState.AddModelError($"Command.{field}", message);
            }
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        return View("Addresses", new AddressPageViewModel { Addresses = await addresses.ListAsync(UserId, cancellationToken), Command = command, EditingId = id });
    }

    [HttpPost("account/addresses/{id:int}/delete")]
    public async Task<IActionResult> DeleteAddress(int id, CancellationToken cancellationToken)
    {
        await addresses.DeleteAsync(UserId, id, cancellationToken);
        TempData[AccountController.StatusMessageKey] = "Đã xóa địa chỉ.";
        return RedirectToAction(nameof(Addresses));
    }

    [HttpPost("account/addresses/{id:int}/default")]
    public async Task<IActionResult> DefaultAddress(int id, CancellationToken cancellationToken)
    {
        await addresses.SetDefaultAsync(UserId, id, cancellationToken);
        TempData[AccountController.StatusMessageKey] = "Đã đặt làm địa chỉ mặc định.";
        return RedirectToAction(nameof(Addresses));
    }

    // ------------------------------------------------------------------ Wishlist

    [HttpGet("wishlist")]
    public async Task<IActionResult> Wishlist(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        return View(await wishlist.GetAsync(UserId, cancellationToken));
    }
}
