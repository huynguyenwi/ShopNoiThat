using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Qr;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Web.Controllers;

/// <summary>
/// QR codes of products and orders: the images, and the short addresses they contain (see <see cref="QrLinks"/>).
/// </summary>
[AllowAnonymous]
public sealed class QrController(IQrCodeRenderer renderer, IQrLookupService lookup, IOptions<ApplicationSettings> site) : Controller
{
    private const string ImageCacheControl = "public,max-age=86400";

    private string BaseUrl => site.Value.BaseUrl;

    // ------------------------------------------------------------------ Scanned addresses

    /// <summary>Product QR: the current product page (the slug may have changed since the label was printed).</summary>
    [HttpGet("/q/p/{id:int}")]
    public async Task<IActionResult> Product(int id, CancellationToken cancellationToken)
    {
        var product = await lookup.FindProductAsync(id, cancellationToken);
        if (product?.PublicUrl is not null)
        {
            return Redirect(product.PublicUrl);
        }

        // Draft / hidden products only open for admins, in the back office.
        return product is not null && User.IsInRole(AppRoles.Admin) ? Redirect($"/admin/products/edit/{product.Id}") : NotFound();
    }

    /// <summary>
    /// Order QR: admins get the back-office order page, the customer who placed it gets their order page, anyone else a
    /// sign-in page (then the same checks) or "not found". Nothing is looked up before sign-in, so scanning or guessing
    /// codes tells an anonymous visitor nothing.
    /// </summary>
    [HttpGet("/q/o/{code}")]
    public async Task<IActionResult> Order(string code, CancellationToken cancellationToken)
    {
        if (!QrLinks.IsOrderCode(code.ToUpperInvariant()))
        {
            return NotFound();
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge(); // cookie auth: /account/login?ReturnUrl=/q/o/{code}
        }

        var order = await lookup.FindOrderAsync(code, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (User.IsInRole(AppRoles.Admin))
        {
            return Redirect($"/admin/orders/details/{order.Id}");
        }

        return order.UserId == User.UserId() ? Redirect($"/account/orders/{order.OrderCode}") : NotFound();
    }

    // ------------------------------------------------------------------ Images

    [HttpGet("/qr/products/{id:int}.svg")]
    public IActionResult ProductSvg(int id) => Svg(QrLinks.ProductTarget(BaseUrl, id));

    [HttpGet("/qr/products/{id:int}.png")]
    public IActionResult ProductPng(int id, int? scale, bool download = false) =>
        Png(QrLinks.ProductTarget(BaseUrl, id), scale, download ? $"qr-san-pham-{id}.png" : null);

    /// <summary>
    /// Public so that confirmation e-mails can show it. It holds only the order address, and it is drawn for any
    /// well-formed code without a database lookup, so it cannot be used to find out which orders exist.
    /// </summary>
    [HttpGet("/qr/orders/{code}.svg")]
    public IActionResult OrderSvg(string code) =>
        QrLinks.IsOrderCode(code) ? Svg(QrLinks.OrderTarget(BaseUrl, code)) : NotFound();

    [HttpGet("/qr/orders/{code}.png")]
    public IActionResult OrderPng(string code, int? scale, bool download = false) =>
        QrLinks.IsOrderCode(code) ? Png(QrLinks.OrderTarget(BaseUrl, code), scale, download ? $"qr-don-hang-{code}.png" : null) : NotFound();

    private ContentResult Svg(string target)
    {
        Response.Headers.CacheControl = ImageCacheControl;
        return Content(renderer.ToSvg(target), "image/svg+xml");
    }

    private FileContentResult Png(string target, int? scale, string? downloadName)
    {
        Response.Headers.CacheControl = ImageCacheControl;
        var bytes = renderer.ToPng(target, scale ?? QrLinks.DefaultPngScale);
        return downloadName is null ? File(bytes, "image/png") : File(bytes, "image/png", downloadName);
    }
}
