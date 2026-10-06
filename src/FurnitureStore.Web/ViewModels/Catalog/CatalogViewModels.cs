using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Engagement;
using Microsoft.AspNetCore.WebUtilities;

namespace FurnitureStore.Web.ViewModels.Catalog;

/// <summary>
/// Query string of /products and GET /api/products, e.g.
/// /products?q=ban+go&amp;category=phong-an&amp;color=nau-oc-cho&amp;minPrice=5000000&amp;sort=price-asc&amp;page=2
/// </summary>
public sealed class ProductListRequest
{
    public const int DefaultPageSize = 12;

    public string? Q { get; set; }
    public string? Category { get; set; }
    public string? Type { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public List<string> Color { get; set; } = [];
    public List<string> Material { get; set; } = [];
    public List<string> Size { get; set; } = [];
    public List<string> Style { get; set; } = [];
    public int? Rating { get; set; }
    public bool OnSale { get; set; }
    public bool InStock { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int? PageSize { get; set; }

    public static readonly IReadOnlyList<(string Key, string Label)> SortOptions =
    [
        ("newest", "Mới nhất"),
        ("price-asc", "Giá thấp đến cao"),
        ("price-desc", "Giá cao đến thấp"),
        ("bestselling", "Bán chạy"),
        ("rating", "Đánh giá cao")
    ];

    public ProductSort SortValue => Sort?.ToLowerInvariant() switch
    {
        "price-asc" => ProductSort.PriceAsc,
        "price-desc" => ProductSort.PriceDesc,
        "bestselling" => ProductSort.BestSelling,
        "rating" => ProductSort.Rating,
        _ => ProductSort.Newest
    };

    public ProductQuery ToQuery() => new()
    {
        Search = Q,
        CategorySlug = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim().ToLowerInvariant(),
        FurnitureType = FurnitureTypes.FromKey(Type),
        MinPrice = MinPrice,
        MaxPrice = MaxPrice,
        ColorSlugs = Clean(Color),
        MaterialSlugs = Clean(Material),
        SizeSlugs = Clean(Size),
        StyleSlugs = Clean(Style),
        MinRating = Rating,
        OnSale = OnSale,
        InStock = InStock,
        Sort = SortValue,
        Page = Page,
        PageSize = PageSize ?? DefaultPageSize
    };

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Q) || !string.IsNullOrWhiteSpace(Category) || !string.IsNullOrWhiteSpace(Type) ||
        MinPrice.HasValue || MaxPrice.HasValue || Color.Count > 0 || Material.Count > 0 || Size.Count > 0 ||
        Style.Count > 0 || Rating.HasValue || OnSale || InStock;

    /// <summary>Builds /products?... for the current filters with optional overrides (null value removes a key).</summary>
    public string Url(IDictionary<string, string?>? overrides = null, (string Key, string Value)? remove = null)
    {
        var pairs = new List<KeyValuePair<string, string?>>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !(remove is { } r && r.Key == key && r.Value == value))
            {
                pairs.Add(new(key, value));
            }
        }

        Add("q", Q);
        Add("category", Category);
        Add("type", Type);
        Add("minPrice", MinPrice?.ToString("0"));
        Add("maxPrice", MaxPrice?.ToString("0"));
        Color.ForEach(v => Add("color", v));
        Material.ForEach(v => Add("material", v));
        Size.ForEach(v => Add("size", v));
        Style.ForEach(v => Add("style", v));
        Add("rating", Rating?.ToString());
        Add("onSale", OnSale ? "true" : null);
        Add("inStock", InStock ? "true" : null);
        Add("sort", Sort);
        Add("page", Page > 1 ? Page.ToString() : null);

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                pairs.RemoveAll(p => p.Key == key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    pairs.Add(new(key, value));
                }
            }
        }

        return QueryHelpers.AddQueryString("/products", pairs);
    }

    public string PageUrl(int page) => Url(new Dictionary<string, string?> { ["page"] = page > 1 ? page.ToString() : null });

    private static List<string> Clean(IEnumerable<string> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim().ToLowerInvariant()).Distinct().Take(20).ToList();
}

public sealed record ActiveFilterChip(string Label, string RemoveUrl);

public sealed class ProductListViewModel
{
    public required ProductListRequest Request { get; init; }
    public required PagedResult<ProductCardDto> Result { get; init; }
    public required FilterOptionsDto Options { get; init; }
    public CategoryNodeDto? Category { get; init; }

    public string Heading =>
        !string.IsNullOrWhiteSpace(Request.Q) ? $"Kết quả tìm kiếm \"{Request.Q}\""
        : Category is not null ? Category.Name
        : FurnitureTypes.FromKey(Request.Type) is { } type ? FurnitureTypes.NameOf(type)
        : Request.OnSale ? "Sản phẩm khuyến mãi"
        : "Tất cả sản phẩm";

    public IReadOnlyList<ActiveFilterChip> Chips
    {
        get
        {
            var chips = new List<ActiveFilterChip>();
            var r = Request;
            string Without(string key) => r.Url(new Dictionary<string, string?> { [key] = null, ["page"] = null });
            string WithoutValue(string key, string value) => r.Url(new Dictionary<string, string?> { ["page"] = null }, (key, value));

            if (!string.IsNullOrWhiteSpace(r.Q)) chips.Add(new($"Từ khóa: {r.Q}", Without("q")));
            if (Category is not null) chips.Add(new(Category.Name, Without("category")));
            if (FurnitureTypes.FromKey(r.Type) is { } type) chips.Add(new(FurnitureTypes.NameOf(type), Without("type")));
            if (r.MinPrice.HasValue) chips.Add(new($"Từ {Infrastructure.Format.Money(r.MinPrice.Value)}", Without("minPrice")));
            if (r.MaxPrice.HasValue) chips.Add(new($"Đến {Infrastructure.Format.Money(r.MaxPrice.Value)}", Without("maxPrice")));
            foreach (var v in r.Color) chips.Add(new(Options.Colors.FirstOrDefault(o => o.Slug == v)?.Name ?? v, WithoutValue("color", v)));
            foreach (var v in r.Material) chips.Add(new(Options.Materials.FirstOrDefault(o => o.Slug == v)?.Name ?? v, WithoutValue("material", v)));
            foreach (var v in r.Size) chips.Add(new(Options.Sizes.FirstOrDefault(o => o.Slug == v)?.Name ?? v, WithoutValue("size", v)));
            foreach (var v in r.Style) chips.Add(new(Options.Styles.FirstOrDefault(o => o.Slug == v)?.Name ?? v, WithoutValue("style", v)));
            if (r.Rating.HasValue) chips.Add(new($"Từ {r.Rating} sao", Without("rating")));
            if (r.OnSale) chips.Add(new("Đang giảm giá", Without("onSale")));
            if (r.InStock) chips.Add(new("Còn hàng", Without("inStock")));
            return chips;
        }
    }
}

public sealed record ProductDetailViewModel(ProductDetailDto Product, IReadOnlyList<ProductCardDto> Related, int? SelectedVariantId)
{
    public VariantDto? SelectedVariant =>
        Product.Variants.FirstOrDefault(v => v.Id == SelectedVariantId) ?? Product.DefaultVariant;

    public required ProductReviewsDto Reviews { get; init; }

    public required ReviewEligibilityDto ReviewEligibility { get; init; }

    /// <summary>Values of the review form (pre-filled with the user's existing review, or re-posted values on error).</summary>
    public ReviewCommand ReviewForm { get; init; } = new();

    public bool ShowReviewForm { get; init; }
}

/// <summary>Model of the _ReviewList partial (first render and AJAX paging).</summary>
public sealed record ReviewListViewModel(string ProductSlug, PagedResult<ReviewDto> Reviews)
{
    public string PageUrl(int page) => $"/products/{ProductSlug}?reviewPage={page}#reviews";
}
