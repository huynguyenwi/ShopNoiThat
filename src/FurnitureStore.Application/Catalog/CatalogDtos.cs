using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Catalog;

/// <summary>Product as shown on cards / lists.</summary>
public sealed record ProductCardDto(
    int Id,
    string Name,
    string Slug,
    string Sku,
    string CategoryName,
    string CategorySlug,
    string? ImageUrl,
    decimal Price,
    decimal? OriginalPrice,
    int DiscountPercent,
    decimal AverageRating,
    int ReviewCount,
    int SoldCount,
    bool InStock,
    bool IsNew,
    bool IsFeatured,
    string? StyleName,
    IReadOnlyList<string> ColorHexes)
{
    public string Url => $"/products/{Slug}";
}

public sealed record CategoryRefDto(int Id, string Name, string Slug);

/// <summary>A product page for sitemap.xml.</summary>
public sealed record SitemapEntryDto(string Slug, DateTime LastModifiedUtc);

public sealed record OptionDto(int Id, string Name, string Slug, string? Hex = null, string? Description = null);

public sealed record ProductImageDto(int Id, string Url, string? AltText, int? VariantId, bool IsPrimary);

/// <summary>Secondary component of a variant, e.g. { Kind = "Chất liệu", Name = "Gỗ sồi", Part = "Khung" }.</summary>
public sealed record VariantPartDto(string Kind, string Name, string? Part, string? Hex);

public sealed record VariantDto(
    int Id,
    string Sku,
    string Name,
    decimal Price,
    decimal? OriginalPrice,
    int DiscountPercent,
    int Stock,
    bool IsLowStock,
    bool IsDefault,
    int? ColorId,
    int? MaterialId,
    int? SizeId,
    int? StyleId,
    string? DimensionsText,
    IReadOnlyList<VariantPartDto> Parts,
    IReadOnlyList<string> ImageUrls);

public sealed record ProductDetailDto(
    int Id,
    string Name,
    string Slug,
    string Sku,
    string? ShortDescription,
    string? Description,
    FurnitureType FurnitureType,
    CategoryRefDto Category,
    CategoryRefDto? ParentCategory,
    OptionDto? Style,
    decimal Price,
    decimal? OriginalPrice,
    int DiscountPercent,
    decimal AverageRating,
    int ReviewCount,
    int SoldCount,
    int StockQuantity,
    int? LengthMm,
    int? WidthMm,
    int? HeightMm,
    decimal? WeightKg,
    int WarrantyMonths,
    string? Origin,
    string? CareInstructions,
    string? MetaTitle,
    string? MetaDescription,
    IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<VariantDto> Variants,
    IReadOnlyList<OptionDto> ColorOptions,
    IReadOnlyList<OptionDto> MaterialOptions,
    IReadOnlyList<OptionDto> SizeOptions,
    IReadOnlyList<OptionDto> StyleOptions)
{
    public string Url => $"/products/{Slug}";
    public VariantDto? DefaultVariant => Variants.FirstOrDefault(v => v.IsDefault) ?? Variants.FirstOrDefault();
    public string? PrimaryImageUrl => Images.FirstOrDefault(i => i.IsPrimary)?.Url ?? Images.FirstOrDefault()?.Url;
}

public sealed record CategoryNodeDto(
    int Id,
    string Name,
    string Slug,
    string? Description,
    string? IconCssClass,
    int ProductCount,
    IReadOnlyList<CategoryNodeDto> Children);

public sealed record FilterOptionsDto(
    IReadOnlyList<CategoryNodeDto> Categories,
    IReadOnlyList<OptionDto> Colors,
    IReadOnlyList<OptionDto> Materials,
    IReadOnlyList<OptionDto> Sizes,
    IReadOnlyList<OptionDto> Styles,
    decimal MinPrice,
    decimal MaxPrice);

public sealed record SearchSuggestionDto(int Id, string Name, string Slug, string? ImageUrl, decimal Price, string CategoryName)
{
    public string Url => $"/products/{Slug}";
}

public sealed record RoomSectionDto(CategoryNodeDto Room, IReadOnlyList<ProductCardDto> Products);

public sealed record HomePageDto(
    IReadOnlyList<ProductCardDto> Featured,
    IReadOnlyList<ProductCardDto> NewArrivals,
    IReadOnlyList<ProductCardDto> BestSellers,
    IReadOnlyList<ProductCardDto> OnSale,
    IReadOnlyList<RoomSectionDto> Rooms);
