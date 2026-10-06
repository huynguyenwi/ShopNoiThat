using FurnitureStore.Domain.Common;
using FurnitureStore.Domain.Exceptions;

namespace FurnitureStore.Domain.Entities;

/// <summary>
/// A purchasable option of a product (SKU): a combination of style, colors, materials and sizes
/// with its own price, stock and images. One primary value per dimension drives the selector on
/// the product page; non-primary rows describe secondary parts (e.g. oak frame of a fabric sofa).
/// </summary>
public class ProductVariant : AuditableEntity, IConcurrencyAware
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int? StyleId { get; set; }
    public ProductStyle? Style { get; set; }

    /// <summary>Human readable label, e.g. "Nâu óc chó / 1m8".</summary>
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;

    public decimal Price { get; set; }

    /// <summary>Old price shown struck-through; null when the variant is not discounted.</summary>
    public decimal? OriginalPrice { get; set; }

    public int StockQuantity { get; set; }
    public int LowStockThreshold { get; set; } = 3;
    public decimal? WeightKg { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    public Guid Version { get; set; }

    public ICollection<ProductVariantColor> Colors { get; set; } = new List<ProductVariantColor>();
    public ICollection<ProductVariantMaterial> Materials { get; set; } = new List<ProductVariantMaterial>();
    public ICollection<ProductVariantSize> Sizes { get; set; } = new List<ProductVariantSize>();
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();

    public bool IsOnSale => OriginalPrice.HasValue && OriginalPrice.Value > Price;

    public int DiscountPercent => IsOnSale
        ? (int)Math.Round((OriginalPrice!.Value - Price) / OriginalPrice.Value * 100m, MidpointRounding.AwayFromZero)
        : 0;

    public bool IsInStock => StockQuantity > 0;

    public bool IsLowStock => StockQuantity <= LowStockThreshold;

    public bool CanFulfil(int quantity) => IsActive && quantity > 0 && quantity <= StockQuantity;

    public void DecreaseStock(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Số lượng phải lớn hơn 0.");
        }

        if (quantity > StockQuantity)
        {
            throw new DomainException($"Sản phẩm \"{Name}\" chỉ còn {StockQuantity} sản phẩm trong kho.");
        }

        StockQuantity -= quantity;
    }

    public void IncreaseStock(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Số lượng phải lớn hơn 0.");
        }

        StockQuantity += quantity;
    }
}
