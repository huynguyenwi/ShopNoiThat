using FurnitureStore.Domain.Common;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Domain.Entities;

/// <summary>Color master data (table ProductColors).</summary>
public class ProductColor : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    /// <summary>Hex code "#RRGGBB" used for swatches.</summary>
    public string HexCode { get; set; } = "#000000";

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ProductVariantColor> VariantColors { get; set; } = new List<ProductVariantColor>();
}

/// <summary>Material master data (table ProductMaterials).</summary>
public class ProductMaterial : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public MaterialGroup Group { get; set; }
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ProductVariantMaterial> VariantMaterials { get; set; } = new List<ProductVariantMaterial>();
}

/// <summary>Size master data (table ProductSizes). Dimensions in millimetres: length x width x height.</summary>
public class ProductSize : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int LengthMm { get; set; }
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }

    /// <summary>Furniture type this size is meant for; null = generic.</summary>
    public FurnitureType? FurnitureType { get; set; }

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ProductVariantSize> VariantSizes { get; set; } = new List<ProductVariantSize>();

    public string DimensionsText => $"{LengthMm} x {WidthMm} x {HeightMm} mm";
}

/// <summary>Design style / "mẫu mã" master data (table ProductStyles), e.g. Modern, Japandi.</summary>
public class ProductStyle : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable English code used by AI prompts and filters, e.g. "Scandinavian".</summary>
    public string Code { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
}

/// <summary>Variant ↔ color. <see cref="Part"/> names the component, e.g. "Khung", "Nệm".</summary>
public class ProductVariantColor
{
    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public int ColorId { get; set; }
    public ProductColor Color { get; set; } = null!;
    public bool IsPrimary { get; set; }
    public string? Part { get; set; }
}

/// <summary>Variant ↔ material. <see cref="Part"/> names the component, e.g. "Mặt bàn", "Chân".</summary>
public class ProductVariantMaterial
{
    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public int MaterialId { get; set; }
    public ProductMaterial Material { get; set; } = null!;
    public bool IsPrimary { get; set; }
    public string? Part { get; set; }
}

/// <summary>Variant ↔ size. Sets (bàn + ghế) can carry one size per component.</summary>
public class ProductVariantSize
{
    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public int SizeId { get; set; }
    public ProductSize Size { get; set; } = null!;
    public bool IsPrimary { get; set; }
    public string? Part { get; set; }
}
