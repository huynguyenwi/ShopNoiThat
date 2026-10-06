using FurnitureStore.Domain.Common;

namespace FurnitureStore.Domain.Entities;

/// <summary>Product gallery image. When <see cref="ProductVariantId"/> is set the image belongs to that variant.</summary>
public class ProductImage : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
}
