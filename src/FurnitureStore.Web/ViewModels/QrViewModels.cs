using FurnitureStore.Application.Sales;

namespace FurnitureStore.Web.ViewModels;

/// <summary>A QR code box (product / order): the image, what scanning does, download and print buttons.</summary>
/// <param name="Admin">Back-office card style; otherwise the storefront "panel" style.</param>
/// <param name="TargetUrl">The address inside the code, shown to admins so they can check it.</param>
public sealed record QrCardModel(
    string Title,
    string ImageUrl,
    string ImageAlt,
    string DownloadUrl,
    string Hint,
    bool Admin,
    string? TargetUrl = null,
    string? PrintUrl = null,
    string PrintLabel = "In");

public sealed record QrLabel(int ProductId, string Name, string Sku, decimal Price, decimal? OriginalPrice);

/// <summary>Printable sheet of product QR labels (/admin/products/qrlabels).</summary>
public sealed record QrLabelsViewModel(IReadOnlyList<QrLabel> Labels, int TotalCount, string StoreName, string StoreHotline, string BackUrl);

/// <summary>Printable delivery slip of an order, with its QR code (/admin/orders/print/{id}).</summary>
public sealed record OrderSlipViewModel(OrderDetailDto Order, string StoreName, string StoreAddress, string StoreHotline);
