using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Sales;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers;

/// <summary>/cart - plain form posts, so the cart works even without JavaScript (cart.js refreshes the page content in place).</summary>
[Route("cart")]
public sealed class CartController(ICartService cart, CartOwnerResolver owners) : Controller
{
    public const string MessageKey = "CartMessage";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        var owner = owners.Resolve();
        var cartDto = await cart.GetAsync(owner, cancellationToken);
        ViewData["CouponOffers"] = await cart.GetOffersAsync(owner, cartDto, cancellationToken);
        return View(cartDto);
    }

    /// <summary>GET /cart/content - the cart body alone; cart.js reloads it after ticking / unticking lines.</summary>
    [HttpGet("content")]
    public async Task<IActionResult> Content(CancellationToken cancellationToken)
    {
        var owner = owners.Resolve();
        var cartDto = await cart.GetAsync(owner, cancellationToken);
        ViewData["CouponOffers"] = await cart.GetOffersAsync(owner, cartDto, cancellationToken);
        return PartialView("_CartContent", cartDto);
    }

    /// <summary>Ticks / unticks a line: only ticked lines go into the next order.</summary>
    [HttpPost("items/{id:int}/select")]
    public Task<IActionResult> Select(int id, bool selected, CancellationToken cancellationToken) =>
        Run(() => cart.SetSelectedAsync(owners.Resolve(), id, selected, cancellationToken), null);

    [HttpPost("select-all")]
    public Task<IActionResult> SelectAll(bool selected, CancellationToken cancellationToken) =>
        Run(() => cart.SelectAllAsync(owners.Resolve(), selected, cancellationToken), null);

    [HttpPost("items/{id:int}/quantity")]
    public Task<IActionResult> UpdateQuantity(int id, int quantity, CancellationToken cancellationToken) =>
        Run(() => cart.UpdateQuantityAsync(owners.Resolve(), id, quantity, cancellationToken), "Đã cập nhật số lượng.");

    [HttpPost("items/{id:int}/remove")]
    public Task<IActionResult> Remove(int id, CancellationToken cancellationToken) =>
        Run(() => cart.RemoveAsync(owners.Resolve(), id, cancellationToken), "Đã xóa sản phẩm khỏi giỏ hàng.");

    /// <param name="returnUrl">"/checkout" when posted from the checkout page (without JavaScript); anything else goes back to the cart.</param>
    [HttpPost("coupon")]
    public Task<IActionResult> ApplyCoupon(string? code, string? returnUrl, CancellationToken cancellationToken) =>
        Run(() => cart.ApplyCouponAsync(owners.Resolve(), code ?? string.Empty, cancellationToken), "Đã áp dụng mã giảm giá.", returnUrl);

    [HttpPost("coupon/remove")]
    public Task<IActionResult> RemoveCoupon(string? returnUrl, CancellationToken cancellationToken) =>
        Run(() => cart.RemoveCouponAsync(owners.Resolve(), cancellationToken), "Đã bỏ mã giảm giá.", returnUrl);

    private async Task<IActionResult> Run(Func<Task<CartDto>> action, string? successMessage, string? returnUrl = null)
    {
        try
        {
            ModelState.ThrowIfBindingFailed(); // e.g. quantity "abc" would otherwise become 0 and remove the line
            await action();
            if (successMessage is not null)
            {
                TempData[MessageKey] = successMessage;
            }
        }
        catch (Exception ex) when (ex is BusinessRuleException or AppValidationException or NotFoundException)
        {
            TempData[MessageKey] = "Lỗi: " + (ex is AppValidationException validation ? string.Join(" ", validation.Errors) : ex.Message);
        }

        // Fixed list, not Url.IsLocalUrl: the only other page showing the coupon box is the checkout.
        return returnUrl == CheckoutPath ? Redirect(CheckoutPath) : RedirectToAction(nameof(Index));
    }

    private const string CheckoutPath = "/checkout";
}
