using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Domain.Entities;

namespace FurnitureStore.Application.Qr;

/// <summary>
/// What product and order QR codes point to. The codes hold short, stable addresses resolved when scanned
/// (QrController): a product label keeps working after the product is renamed (its slug changes), and an order code
/// reveals nothing to whoever scans it without being signed in as the customer or an admin.
/// Images are generated on demand from these addresses; nothing is stored.
/// </summary>
public static class QrLinks
{
    public const int DefaultPngScale = 8;
    public const int MinPngScale = 2;
    public const int MaxPngScale = 20;

    public static string ProductTarget(string baseUrl, int productId) => $"{Root(baseUrl)}/q/p/{productId}";

    public static string OrderTarget(string baseUrl, string orderCode) => $"{Root(baseUrl)}/q/o/{orderCode}";

    /// <summary>Site-relative image address; <paramref name="format"/> is "svg" (pages) or "png" (downloads, print, e-mail).</summary>
    public static string ProductImage(int productId, string format = "svg") => $"/qr/products/{productId}.{format}";

    public static string OrderImage(string orderCode, string format = "svg") => $"/qr/orders/{orderCode}.{format}";

    /// <summary>Order codes look like DH260930-4A7NA: upper-case letters, digits and dashes.</summary>
    public static bool IsOrderCode(string? code) =>
        code is { Length: >= 4 and <= 30 } && code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');

    private static string Root(string baseUrl) => baseUrl.TrimEnd('/');
}

/// <summary>Draws a QR code for a text (implemented in Infrastructure).</summary>
public interface IQrCodeRenderer
{
    string ToSvg(string text);

    /// <param name="scale">Pixels per QR module.</param>
    byte[] ToPng(string text, int scale);
}

/// <param name="PublicUrl">Product page, or null when the product exists but is not on sale (draft / hidden).</param>
public sealed record ProductQrTarget(int Id, string? PublicUrl);

public sealed record OrderQrTarget(int Id, string OrderCode, string UserId);

/// <summary>Looks up what a scanned QR code refers to.</summary>
public interface IQrLookupService
{
    Task<ProductQrTarget?> FindProductAsync(int id, CancellationToken cancellationToken = default);

    Task<OrderQrTarget?> FindOrderAsync(string orderCode, CancellationToken cancellationToken = default);
}

public sealed class QrLookupService(IRepository<Product> products, IRepository<Order> orders) : IQrLookupService
{
    public async Task<ProductQrTarget?> FindProductAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdAsync(id, cancellationToken); // deleted products are filtered out
        return product is null ? null : new ProductQrTarget(product.Id, product.IsVisibleToCustomers ? $"/products/{product.Slug}" : null);
    }

    public async Task<OrderQrTarget?> FindOrderAsync(string orderCode, CancellationToken cancellationToken = default)
    {
        var code = (orderCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!QrLinks.IsOrderCode(code))
        {
            return null;
        }

        var order = (await orders.ListAsync(o => o.OrderCode == code, cancellationToken)).FirstOrDefault();
        return order is null ? null : new OrderQrTarget(order.Id, order.OrderCode, order.UserId);
    }
}
