using FurnitureStore.Application.Sales;
using FurnitureStore.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.ViewComponents;

/// <summary>Cart icon with the number of items (updated live by cart.js after add-to-cart).</summary>
public sealed class CartBadgeViewComponent(ICartService cart, CartOwnerResolver owners) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string cssClass = "")
    {
        var count = await cart.CountAsync(owners.Resolve(), HttpContext.RequestAborted);
        return View((count, cssClass));
    }
}
