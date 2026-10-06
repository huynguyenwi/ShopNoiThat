using FurnitureStore.Domain.Entities;

namespace FurnitureStore.Application.Catalog;

/// <summary>Maps a loaded product graph to <see cref="ProductDetailDto"/>.</summary>
public static class CatalogMapper
{
    public static ProductDetailDto ToDetail(Product product)
    {
        var variants = product.Variants
            .Where(v => v.IsActive)
            .OrderBy(v => v.DisplayOrder)
            .ThenBy(v => v.Id)
            .ToList();

        var productImages = product.Images
            .Where(i => i.ProductVariantId is null)
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.DisplayOrder)
            .ToList();
        var variantImages = product.Images
            .Where(i => i.ProductVariantId is not null && variants.Any(v => v.Id == i.ProductVariantId))
            .OrderBy(i => i.DisplayOrder)
            .ToList();

        var images = productImages.Concat(variantImages)
            .Select(i => new ProductImageDto(i.Id, i.Url, i.AltText ?? product.Name, i.ProductVariantId, i.IsPrimary))
            .ToList();

        var variantDtos = variants.Select(v =>
        {
            var primaryColor = v.Colors.FirstOrDefault(c => c.IsPrimary);
            var primaryMaterial = v.Materials.FirstOrDefault(m => m.IsPrimary);
            var primarySize = v.Sizes.FirstOrDefault(s => s.IsPrimary);

            var parts = v.Materials.Where(m => !m.IsPrimary)
                .Select(m => new VariantPartDto("Chất liệu", m.Material.Name, m.Part, null))
                .Concat(v.Colors.Where(c => !c.IsPrimary).Select(c => new VariantPartDto("Màu", c.Color.Name, c.Part, c.Color.HexCode)))
                .Concat(v.Sizes.Where(s => !s.IsPrimary).Select(s => new VariantPartDto("Kích thước", s.Size.Name, s.Part, null)))
                .ToList();

            var ownImages = variantImages.Where(i => i.ProductVariantId == v.Id).Select(i => i.Url).ToList();
            var dimensions = primarySize is not null
                ? primarySize.Size.DimensionsText
                : FormatDimensions(product.LengthMm, product.WidthMm, product.HeightMm);

            return new VariantDto(
                v.Id, v.Sku, v.Name, v.Price, v.IsOnSale ? v.OriginalPrice : null, v.DiscountPercent,
                v.StockQuantity, v.IsLowStock, v.IsDefault,
                primaryColor?.ColorId, primaryMaterial?.MaterialId, primarySize?.SizeId, v.StyleId,
                dimensions, parts, ownImages);
        }).ToList();

        // Guarantee exactly one default so the page always pre-selects something buyable.
        if (variantDtos.Count > 0 && !variantDtos.Any(v => v.IsDefault))
        {
            var fallback = variantDtos.FirstOrDefault(v => v.Stock > 0) ?? variantDtos[0];
            variantDtos[variantDtos.IndexOf(fallback)] = fallback with { IsDefault = true };
        }

        var colorOptions = variants.SelectMany(v => v.Colors.Where(c => c.IsPrimary).Select(c => c.Color))
            .DistinctBy(c => c.Id).OrderBy(c => c.DisplayOrder)
            .Select(c => new OptionDto(c.Id, c.Name, c.Slug, c.HexCode)).ToList();
        var materialOptions = variants.SelectMany(v => v.Materials.Where(m => m.IsPrimary).Select(m => m.Material))
            .DistinctBy(m => m.Id).OrderBy(m => m.DisplayOrder)
            .Select(m => new OptionDto(m.Id, m.Name, m.Slug, Description: m.Description)).ToList();
        var sizeOptions = variants.SelectMany(v => v.Sizes.Where(s => s.IsPrimary).Select(s => s.Size))
            .DistinctBy(s => s.Id).OrderBy(s => s.DisplayOrder)
            .Select(s => new OptionDto(s.Id, s.Name, s.Slug, Description: s.DimensionsText)).ToList();
        var styleOptions = variants.Where(v => v.Style is not null).Select(v => v.Style!)
            .DistinctBy(s => s.Id).OrderBy(s => s.DisplayOrder)
            .Select(s => new OptionDto(s.Id, s.Name, s.Slug, Description: s.Description)).ToList();

        var category = product.Category;
        return new ProductDetailDto(
            product.Id, product.Name, product.Slug, product.Sku, product.ShortDescription, product.Description,
            product.FurnitureType,
            new CategoryRefDto(category.Id, category.Name, category.Slug),
            category.Parent is null ? null : new CategoryRefDto(category.Parent.Id, category.Parent.Name, category.Parent.Slug),
            product.Style is null ? null : new OptionDto(product.Style.Id, product.Style.Name, product.Style.Slug, Description: product.Style.Description),
            product.EffectivePrice, product.IsOnSale ? product.BasePrice : null, product.DiscountPercent,
            product.AverageRating, product.ReviewCount, product.SoldCount, product.StockQuantity,
            product.LengthMm, product.WidthMm, product.HeightMm, product.WeightKg, product.WarrantyMonths,
            product.Origin, product.CareInstructions, product.MetaTitle, product.MetaDescription,
            images, variantDtos, colorOptions, materialOptions, sizeOptions, styleOptions);
    }

    public static string? FormatDimensions(int? length, int? width, int? height) =>
        length.HasValue && width.HasValue && height.HasValue ? $"{length} x {width} x {height} mm" : null;
}
