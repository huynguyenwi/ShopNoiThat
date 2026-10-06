using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.ViewModels.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers;

/// <summary>
/// /checkout - "Liên hệ đặt hàng": the customer leaves contact and delivery details, the store calls back to confirm the order
/// and quote delivery &amp; installation. Requires sign-in (guests are sent to the login page and come back here).
/// </summary>
[Route("checkout")]
[Authorize(Policy = AuthorizationPolicies.SignedIn)]
public sealed class CheckoutController(
    ICartService cart,
    IOrderService orders,
    IAddressService addresses,
    UserManager<ApplicationUser> userManager) : Controller
{
    private string UserId => User.UserId()!;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var cartDto = await cart.GetAsync(new CartOwner(UserId, null), cancellationToken);
        if (cartDto.IsEmpty)
        {
            TempData[CartController.MessageKey] = "Lỗi: Giỏ hàng đang trống.";
            return Redirect("/cart");
        }

        if (!cartDto.HasSelection)
        {
            TempData[CartController.MessageKey] = "Lỗi: Vui lòng tích chọn sản phẩm muốn đặt.";
            return Redirect("/cart");
        }

        var saved = await addresses.ListAsync(UserId, cancellationToken);
        var user = await userManager.GetUserAsync(User);
        var preferred = saved.FirstOrDefault(a => a.IsDefault) ?? saved.FirstOrDefault();

        var command = new CheckoutCommand
        {
            FullName = preferred?.RecipientName ?? user?.FullName ?? string.Empty,
            Phone = preferred?.Phone ?? user?.PhoneNumber ?? string.Empty,
            Email = user?.Email ?? string.Empty,
            AddressLine = preferred?.AddressLine ?? string.Empty,
            Ward = preferred?.Ward ?? string.Empty,
            Province = preferred?.Province ?? string.Empty,
            SaveAddress = saved.Count == 0
        };

        return View(await BuildAsync(command, cartDto, saved, cancellationToken));
    }

    [HttpPost("")]
    public async Task<IActionResult> Place([Bind(Prefix = "Command")] CheckoutCommand command, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            // No payment step: the customer pays on delivery, or by transfer when the staff who call back ask for it.
            command.PaymentMethod = PaymentMethod.COD;
            var result = await orders.PlaceOrderAsync(UserId, command, cancellationToken);
            // Explicit URL: LowercaseUrls would otherwise lower-case the order code in the route.
            return Redirect($"/checkout/success/{result.OrderCode}");
        }
        catch (AppValidationException ex)
        {
            foreach (var (field, messages) in ex.FieldErrors)
            {
                foreach (var message in messages)
                {
                    ModelState.AddModelError($"Command.{field}", message);
                }
            }
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        var cartDto = await cart.GetAsync(new CartOwner(UserId, null), cancellationToken);
        if (!cartDto.HasSelection)
        {
            TempData[CartController.MessageKey] = "Lỗi: Vui lòng tích chọn sản phẩm muốn đặt.";
            return Redirect("/cart");
        }

        return View("Index", await BuildAsync(command, cartDto, await addresses.ListAsync(UserId, cancellationToken), cancellationToken));
    }

    /// <summary>Order summary column alone: checkout.js reloads it after applying / removing a coupon.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var owner = new CartOwner(UserId, null);
        var cartDto = await cart.GetAsync(owner, cancellationToken);
        // Not cached: the anti-forgery tokens in the coupon forms already make the response "no-cache, no-store".
        return PartialView("_CheckoutSummary", new CheckoutSummaryModel(cartDto, await cart.GetOffersAsync(owner, cartDto, cancellationToken)));
    }

    [HttpGet("success/{code}")]
    public async Task<IActionResult> Success(string code, CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        return View(await orders.GetMyOrderAsync(UserId, code, cancellationToken));
    }

    private async Task<CheckoutViewModel> BuildAsync(CheckoutCommand command, CartDto cartDto, IReadOnlyList<CustomerAddressDto> saved, CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        return new CheckoutViewModel
        {
            Command = command,
            Cart = cartDto,
            SavedAddresses = saved,
            Offers = await cart.GetOffersAsync(new CartOwner(UserId, null), cartDto, cancellationToken)
        };
    }
}
