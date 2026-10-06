using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Catalog;

public enum ProductSort
{
    Newest = 0,
    PriceAsc = 1,
    PriceDesc = 2,
    BestSelling = 3,
    Rating = 4
}

/// <summary>Filters for the public product list (/products and GET /api/products).</summary>
public sealed class ProductQuery
{
    public const int MaxPageSize = 48;

    public string? Search { get; set; }

    /// <summary>Category slug; a room category also includes its sub-categories.</summary>
    public string? CategorySlug { get; set; }

    public FurnitureType? FurnitureType { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public IReadOnlyList<string> ColorSlugs { get; set; } = [];
    public IReadOnlyList<string> MaterialSlugs { get; set; } = [];
    public IReadOnlyList<string> SizeSlugs { get; set; } = [];
    public IReadOnlyList<string> StyleSlugs { get; set; } = [];
    public int? MinRating { get; set; }
    public bool OnSale { get; set; }
    public bool InStock { get; set; }
    public bool FeaturedOnly { get; set; }

    /// <summary>Restricts the list to these products (wishlist, AI recommendations).</summary>
    public IReadOnlyCollection<int>? ProductIds { get; set; }
    public ProductSort Sort { get; set; } = ProductSort.Newest;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;

    /// <summary>Clamps paging and price values to safe ranges.</summary>
    public ProductQuery Normalize()
    {
        Page = Math.Max(1, Page);
        PageSize = Math.Clamp(PageSize, 1, MaxPageSize);
        if (MinPrice < 0) MinPrice = null;
        if (MaxPrice < 0) MaxPrice = null;
        if (MinPrice.HasValue && MaxPrice.HasValue && MinPrice > MaxPrice)
        {
            (MinPrice, MaxPrice) = (MaxPrice, MinPrice);
        }
        if (MinRating is < 1 or > 5) MinRating = null;
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()[..Math.Min(Search.Trim().Length, 100)];
        return this;
    }
}
