using FurnitureStore.Domain.Common;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Domain.Entities;

/// <summary>
/// A sellable furniture model. Price, stock and SKU that a customer actually buys live on
/// <see cref="ProductVariant"/>; the product keeps denormalized "from" price and total stock
/// (kept in sync by <see cref="SyncFromVariants"/>) so listings can filter and sort cheaply.
/// </summary>
public class Product : AuditableEntity, ISoftDelete, IConcurrencyAware
{
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    /// <summary>Main design style ("mẫu mã"), e.g. Scandinavian.</summary>
    public int? StyleId { get; set; }
    public ProductStyle? Style { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }

    public FurnitureType FurnitureType { get; set; }
    public ProductStatus Status { get; set; } = ProductStatus.Draft;

    /// <summary>List price shown on cards ("giá từ"). Synced from the cheapest active variant.</summary>
    public decimal BasePrice { get; set; }

    /// <summary>Discounted "from" price, null when not on sale.</summary>
    public decimal? DiscountPrice { get; set; }

    /// <summary>Total stock across active variants.</summary>
    public int StockQuantity { get; set; }

    public bool IsFeatured { get; set; }
    public int SoldCount { get; set; }
    public int ViewCount { get; set; }
    public decimal AverageRating { get; set; }
    public int ReviewCount { get; set; }

    /// <summary>Reference dimensions in millimetres (default variant).</summary>
    public int? LengthMm { get; set; }
    public int? WidthMm { get; set; }
    public int? HeightMm { get; set; }
    public decimal? WeightKg { get; set; }

    public int WarrantyMonths { get; set; } = 12;
    public string? Origin { get; set; }
    public string? CareInstructions { get; set; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// Lower-case, accent-free text (name, SKU, category, materials, colors, style) used for search, so
    /// "ban go" matches "Bàn ăn gỗ óc chó". Rebuilt by the application whenever the product changes.
    /// </summary>
    public string SearchText { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public Guid Version { get; set; }

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();

    public bool IsOnSale => DiscountPrice is > 0 && DiscountPrice < BasePrice;

    /// <summary>Price the customer pays for the cheapest option.</summary>
    public decimal EffectivePrice => IsOnSale ? DiscountPrice!.Value : BasePrice;

    public int DiscountPercent => IsOnSale && BasePrice > 0
        ? (int)Math.Round((BasePrice - DiscountPrice!.Value) / BasePrice * 100m, MidpointRounding.AwayFromZero)
        : 0;

    public bool IsInStock => StockQuantity > 0;

    public bool IsVisibleToCustomers => Status == ProductStatus.Active && !IsDeleted;

    /// <summary>
    /// Recomputes denormalized price and stock from active variants.
    /// Call after adding/removing variants or changing their price/stock.
    /// </summary>
    public void SyncFromVariants()
    {
        var active = Variants.Where(v => v.IsActive).ToList();
        if (active.Count == 0)
        {
            StockQuantity = 0;
            return;
        }

        StockQuantity = active.Sum(v => v.StockQuantity);

        var cheapest = active.OrderBy(v => v.Price).ThenBy(v => v.DisplayOrder).First();
        if (cheapest.IsOnSale)
        {
            BasePrice = cheapest.OriginalPrice!.Value;
            DiscountPrice = cheapest.Price;
        }
        else
        {
            BasePrice = cheapest.Price;
            DiscountPrice = null;
        }
    }

    /// <summary>Recomputes AverageRating/ReviewCount from visible reviews.</summary>
    public void ApplyRatingSummary(decimal averageRating, int reviewCount)
    {
        ReviewCount = Math.Max(0, reviewCount);
        AverageRating = reviewCount == 0 ? 0 : Math.Round(Math.Clamp(averageRating, 0m, 5m), 2);
    }
}
