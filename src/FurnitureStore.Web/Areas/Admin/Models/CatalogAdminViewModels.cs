using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Catalog.Admin;
using FurnitureStore.Application.Common.Models;

namespace FurnitureStore.Web.Areas.Admin.Models;

public sealed class AdminProductListViewModel
{
    public required AdminProductQuery Query { get; init; }
    public required PagedResult<AdminProductListItemDto> Result { get; init; }
    public required CatalogLookupsDto Lookups { get; init; }

    public string PageUrl(int page)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Query.Search)) parts.Add("search=" + Uri.EscapeDataString(Query.Search));
        if (Query.CategoryId.HasValue) parts.Add("categoryId=" + Query.CategoryId);
        if (Query.Status.HasValue) parts.Add("status=" + Query.Status);
        if (Query.Stock != StockFilter.All) parts.Add("stock=" + Query.Stock);
        if (page > 1) parts.Add("page=" + page);
        return "/admin/products" + (parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty);
    }
}

public sealed class ProductFormViewModel
{
    public int? Id { get; init; }
    public required ProductUpsertCommand Command { get; init; }
    public required CatalogLookupsDto Lookups { get; init; }
    public IReadOnlyList<ProductImageDto> Images { get; init; } = [];
    public string? Slug { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }

    public bool IsEdit => Id.HasValue;

    /// <summary>Key of the default variant row (radio button group "DefaultVariantKey").</summary>
    public string DefaultVariantKey =>
        Command.Variants.FindIndex(v => v.IsDefault) is var index and >= 0 ? index.ToString() : "0";
}

public sealed class CategoryFormViewModel
{
    public int? Id { get; init; }
    public required CategoryUpsertCommand Command { get; init; }
    public required IReadOnlyList<AdminCategoryDto> Parents { get; init; }
}

public sealed class AttributeListViewModel
{
    public required AttributeKind Kind { get; init; }
    public required IReadOnlyList<AdminAttributeDto> Items { get; init; }
}

public sealed class AttributeFormViewModel
{
    public int? Id { get; init; }
    public required AttributeKind Kind { get; init; }
    public required AttributeUpsertCommand Command { get; init; }
}

public sealed class AdminOrderListViewModel
{
    public required FurnitureStore.Application.Admin.AdminOrderQuery Query { get; init; }
    public required PagedResult<FurnitureStore.Application.Admin.AdminOrderListItemDto> Result { get; init; }
    public required IReadOnlyList<FurnitureStore.Application.Admin.StatusCountDto> StatusCounts { get; init; }

    public string Url(int page = 1, Domain.Enums.OrderStatus? status = null, bool keepStatus = true)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Query.Search)) parts.Add("search=" + Uri.EscapeDataString(Query.Search));
        var effectiveStatus = keepStatus ? status ?? Query.Status : status;
        if (effectiveStatus.HasValue) parts.Add("status=" + effectiveStatus);
        if (Query.PaymentStatus.HasValue) parts.Add("paymentStatus=" + Query.PaymentStatus);
        if (Query.From.HasValue) parts.Add("from=" + Query.From.Value.ToString("yyyy-MM-dd"));
        if (Query.To.HasValue) parts.Add("to=" + Query.To.Value.ToString("yyyy-MM-dd"));
        if (page > 1) parts.Add("page=" + page);
        return "/admin/orders" + (parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty);
    }
}

public sealed class AdminUserListViewModel
{
    public required FurnitureStore.Application.Admin.AdminUserQuery Query { get; init; }
    public required PagedResult<FurnitureStore.Application.Admin.AdminUserListItemDto> Result { get; init; }

    public string PageUrl(int page)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Query.Search)) parts.Add("search=" + Uri.EscapeDataString(Query.Search));
        if (!string.IsNullOrWhiteSpace(Query.Role)) parts.Add("role=" + Uri.EscapeDataString(Query.Role));
        if (Query.Status != FurnitureStore.Application.Admin.UserStatusFilter.All) parts.Add("status=" + Query.Status);
        if (page > 1) parts.Add("page=" + page);
        return "/admin/customers" + (parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty);
    }
}

public sealed class AdminReviewListViewModel
{
    public required FurnitureStore.Application.Engagement.AdminReviewQuery Query { get; init; }
    public required PagedResult<FurnitureStore.Application.Engagement.AdminReviewDto> Result { get; init; }

    public string PageUrl(int page)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Query.Search)) parts.Add("search=" + Uri.EscapeDataString(Query.Search));
        if (Query.Rating.HasValue) parts.Add("rating=" + Query.Rating);
        if (Query.Hidden.HasValue) parts.Add("hidden=" + Query.Hidden.Value.ToString().ToLowerInvariant());
        if (page > 1) parts.Add("page=" + page);
        return "/admin/reviews" + (parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty);
    }
}

public static class AttributeKinds
{
    public static readonly IReadOnlyList<(AttributeKind Kind, string Route, string Title, string Icon)> All =
    [
        (AttributeKind.Color, "colors", "Màu sắc", "bi-palette"),
        (AttributeKind.Material, "materials", "Chất liệu", "bi-layers"),
        (AttributeKind.Size, "sizes", "Kích thước", "bi-rulers"),
        (AttributeKind.Style, "styles", "Mẫu mã / Phong cách", "bi-brush")
    ];

    public static AttributeKind? FromRoute(string? route) =>
        All.FirstOrDefault(k => string.Equals(k.Route, route, StringComparison.OrdinalIgnoreCase)) is { Route: not null } match ? match.Kind : null;

    public static string RouteOf(AttributeKind kind) => All.First(k => k.Kind == kind).Route;

    public static string TitleOf(AttributeKind kind) => All.First(k => k.Kind == kind).Title;
}
