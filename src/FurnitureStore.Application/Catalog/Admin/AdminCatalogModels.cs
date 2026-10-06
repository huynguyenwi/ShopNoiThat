using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Catalog.Admin;

public enum StockFilter
{
    All = 0,
    InStock = 1,
    LowStock = 2,
    OutOfStock = 3
}

public sealed class AdminProductQuery
{
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public ProductStatus? Status { get; set; }
    public StockFilter Stock { get; set; } = StockFilter.All;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record AdminProductListItemDto(
    int Id,
    string Name,
    string Slug,
    string Sku,
    string? ImageUrl,
    string CategoryName,
    ProductStatus Status,
    bool IsFeatured,
    decimal Price,
    decimal? OriginalPrice,
    int StockQuantity,
    int VariantCount,
    int LowStockVariantCount,
    int SoldCount,
    DateTime? UpdatedAt,
    DateTime CreatedAt);

/// <summary>Create / update payload for a product and its variants (Admin form and POST/PUT /api/admin/products).</summary>
public sealed class ProductUpsertCommand
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional; generated from the name when empty.</summary>
    public string? Slug { get; set; }

    public string Sku { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public int? StyleId { get; set; }
    public FurnitureType FurnitureType { get; set; } = FurnitureType.Table;
    public ProductStatus Status { get; set; } = ProductStatus.Draft;
    public bool IsFeatured { get; set; }
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }

    /// <summary>Used for the default variant when no variant is provided.</summary>
    public decimal BasePrice { get; set; }
    public decimal? DiscountPrice { get; set; }
    public int StockQuantity { get; set; }

    public int? LengthMm { get; set; }
    public int? WidthMm { get; set; }
    public int? HeightMm { get; set; }
    public decimal? WeightKg { get; set; }
    public int WarrantyMonths { get; set; } = 12;
    public string? Origin { get; set; }
    public string? CareInstructions { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }

    public List<VariantUpsertModel> Variants { get; set; } = [];

    /// <summary>Concurrency token read with the product; required when updating.</summary>
    public Guid? Version { get; set; }
}

public sealed class VariantUpsertModel
{
    /// <summary>Existing variant id; null for a new variant.</summary>
    public int? Id { get; set; }

    /// <summary>Optional label; generated from color / material / size when empty.</summary>
    public string? Name { get; set; }

    public string Sku { get; set; } = string.Empty;
    public int? ColorId { get; set; }
    public int? MaterialId { get; set; }
    public int? SizeId { get; set; }
    public int? StyleId { get; set; }
    public decimal Price { get; set; }
    public decimal? OriginalPrice { get; set; }
    public int StockQuantity { get; set; }
    public int LowStockThreshold { get; set; } = 3;
    public decimal? WeightKg { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }

    public int? SecondaryMaterialId { get; set; }
    public string? SecondaryMaterialPart { get; set; }
    public int? SecondaryColorId { get; set; }
    public string? SecondaryColorPart { get; set; }
}

public sealed record ProductSavedResult(int ProductId, string Slug, IReadOnlyList<int> VariantIds);

/// <summary>Everything the product edit form needs.</summary>
public sealed record ProductEditDto(
    int Id,
    ProductUpsertCommand Command,
    IReadOnlyList<ProductImageDto> Images,
    string Slug,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? UpdatedBy);

public sealed record ImageUpload(Stream Content, string FileName, int? VariantId = null);

public sealed record CatalogLookupsDto(
    IReadOnlyList<(int Id, string Name, int? ParentId)> Categories,
    IReadOnlyList<OptionDto> Colors,
    IReadOnlyList<OptionDto> Materials,
    IReadOnlyList<OptionDto> Sizes,
    IReadOnlyList<OptionDto> Styles);

// ------------------------------------------------------------------ Categories & attributes

public sealed class CategoryUpsertCommand
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public int? ParentId { get; set; }
    public string? Description { get; set; }
    public string? IconCssClass { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowOnHomePage { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
}

public sealed record AdminCategoryDto(
    int Id,
    string Name,
    string Slug,
    int? ParentId,
    string? ParentName,
    string? Description,
    string? IconCssClass,
    int DisplayOrder,
    bool IsActive,
    bool ShowOnHomePage,
    string? MetaTitle,
    string? MetaDescription,
    int ProductCount,
    int ChildCount);

public enum AttributeKind
{
    Color = 1,
    Material = 2,
    Size = 3,
    Style = 4
}

/// <summary>One payload for the four attribute tables; fields irrelevant to a kind are ignored.</summary>
public sealed class AttributeUpsertCommand
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }

    // Color
    public string? HexCode { get; set; }

    // Material
    public MaterialGroup? MaterialGroup { get; set; }

    // Size
    public int? LengthMm { get; set; }
    public int? WidthMm { get; set; }
    public int? HeightMm { get; set; }
    public FurnitureType? FurnitureType { get; set; }

    // Style
    public string? Code { get; set; }
}

public sealed record AdminAttributeDto(
    int Id,
    AttributeKind Kind,
    string Name,
    string Slug,
    int DisplayOrder,
    bool IsActive,
    int UsageCount,
    string? Description,
    string? HexCode,
    MaterialGroup? MaterialGroup,
    int? LengthMm,
    int? WidthMm,
    int? HeightMm,
    FurnitureType? FurnitureType,
    string? Code);
