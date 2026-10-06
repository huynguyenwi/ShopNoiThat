using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Utilities;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Infrastructure.Persistence.Seed;

/// <summary>
/// Inserts the demo catalog (<see cref="CatalogSeedData"/>) when no product exists yet.
/// Variants are generated as materials × colors × sizes with deterministic SKUs, prices and stock.
/// A catalog seeded by an earlier version is topped up with the demo products added since, and descriptions nobody
/// edited are upgraded (<see cref="CatalogSeedData.DescriptionUpgrades"/>).
/// </summary>
public sealed class CatalogSeeder(ApplicationDbContext context, TimeProvider timeProvider, ILogger<CatalogSeeder> logger)
{
    private const string Origin = "Việt Nam - Xưởng Nhà Mộc";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Deleted products count too: a demo product an admin removed never comes back.
        var existingSkus = await context.Products.IgnoreQueryFilters().Select(p => p.Sku).ToListAsync(cancellationToken);
        if (existingSkus.Count == 0)
        {
            await SeedProductsAsync(CatalogSeedData.Products.Select((seed, index) => (seed, index)).ToList(), onlyNeededLookups: false, cancellationToken);
            return;
        }

        await UpgradeDescriptionsAsync(cancellationToken);
        await TopUpAsync(existingSkus, cancellationToken);
    }

    /// <summary>Adds the demo products missing from a catalog that an earlier version of this seeder created.</summary>
    private async Task TopUpAsync(IReadOnlyCollection<string> existingSkus, CancellationToken cancellationToken)
    {
        var skus = new HashSet<string>(existingSkus, StringComparer.OrdinalIgnoreCase);
        if (!CatalogSeedData.Products.Any(p => skus.Contains(p.Sku)))
        {
            logger.LogDebug("Catalog was not created from the demo data; demo catalog seeding skipped");
            return;
        }

        var slugs = new HashSet<string>(await context.Products.IgnoreQueryFilters().Select(p => p.Slug).ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);
        var missing = new List<(ProductSeed Seed, int Index)>();
        for (var index = 0; index < CatalogSeedData.Products.Length; index++)
        {
            var seed = CatalogSeedData.Products[index];
            var prefix = seed.Sku + "-";
            if (skus.Contains(seed.Sku) || slugs.Contains(SlugGenerator.Generate(seed.Name))
                || await context.ProductVariants.IgnoreQueryFilters().AnyAsync(v => v.Sku.StartsWith(prefix), cancellationToken))
            {
                continue;
            }

            missing.Add((seed, index));
        }

        if (missing.Count > 0)
        {
            await SeedProductsAsync(missing, onlyNeededLookups: true, cancellationToken);
        }
    }

    /// <param name="onlyNeededLookups">
    /// Topping up: add only the categories, colors, materials, styles and sizes these products use, so lookups an admin
    /// deleted are not brought back.
    /// </param>
    private async Task SeedProductsAsync(IReadOnlyList<(ProductSeed Seed, int Index)> seeds, bool onlyNeededLookups, CancellationToken cancellationToken)
    {
        var needed = onlyNeededLookups ? NeededLookups.For(seeds.Select(s => s.Seed)) : null;

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var materialsBefore = await context.ProductMaterials.Select(m => m.Slug).ToListAsync(ct);
            var categories = await SeedCategoriesAsync(needed?.Categories, ct);
            var colors = await SeedLookupAsync(context.ProductColors, CatalogSeedData.Colors, c => c.Slug,
                (seed, i) => new ProductColor { Slug = seed.Slug, Name = seed.Name, HexCode = seed.Hex, DisplayOrder = i }, x => x.Slug, needed?.Colors, ct);
            var materials = await SeedLookupAsync(context.ProductMaterials, CatalogSeedData.Materials, m => m.Slug,
                (seed, i) => new ProductMaterial { Slug = seed.Slug, Name = seed.Name, Group = seed.Group, Description = seed.Description, DisplayOrder = i }, x => x.Slug, needed?.Materials, ct);
            var styles = await SeedLookupAsync(context.ProductStyles, CatalogSeedData.Styles, s => s.Code,
                (seed, i) => new ProductStyle { Code = seed.Code, Slug = seed.Slug, Name = seed.Name, Description = seed.Description, DisplayOrder = i }, x => x.Code, needed?.Styles, ct);
            var sizes = await SeedLookupAsync(context.ProductSizes, CatalogSeedData.Sizes, s => s.Slug,
                (seed, i) => new ProductSize
                {
                    Slug = seed.Slug, Name = seed.Name, LengthMm = seed.LengthMm, WidthMm = seed.WidthMm,
                    HeightMm = seed.HeightMm, FurnitureType = seed.Type, DisplayOrder = i
                }, x => x.Slug, needed?.Sizes, ct);

            // A material this top-up creates also gets its default price in an existing custom-quote price list (only now,
            // so a price an admin deletes later stays deleted).
            if (onlyNeededLookups && await context.PriceRules.AnyAsync(ct))
            {
                foreach (var material in materials.Values.Where(m => !materialsBefore.Contains(m.Slug, StringComparer.OrdinalIgnoreCase)))
                {
                    if (PriceRuleSeeder.MaterialRuleFor(material) is { } rule)
                    {
                        context.PriceRules.Add(rule);
                    }
                }
            }

            var usedSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lookups = new Lookups(categories, colors, materials, styles, sizes);

            foreach (var (seed, index) in seeds)
            {
                context.Products.Add(BuildProduct(seed, index, now, lookups, usedSkus, usedSlugs));
            }

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            logger.LogInformation("Seeded demo catalog: {Products} products, {Variants} variants ({Skus})",
                seeds.Count, usedSkus.Count - seeds.Count, onlyNeededLookups ? string.Join(", ", seeds.Select(s => s.Seed.Sku)) : "full catalog");
        }, cancellationToken);
    }

    private async Task UpgradeDescriptionsAsync(CancellationToken cancellationToken)
    {
        var upgrades = CatalogSeedData.DescriptionUpgrades.ToDictionary(u => u.Sku, StringComparer.OrdinalIgnoreCase);
        var skus = upgrades.Keys.ToList();
        var products = await context.Products.IgnoreQueryFilters().Where(p => skus.Contains(p.Sku)).ToListAsync(cancellationToken);

        var upgraded = 0;
        foreach (var product in products)
        {
            // The SQL setup script may carry Windows line breaks.
            var upgrade = upgrades[product.Sku];
            if (product.Description?.Replace("\r\n", "\n") == upgrade.Old)
            {
                product.Description = upgrade.New;
                upgraded++;
            }
        }

        if (upgraded > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Upgraded the default description of {Count} demo products", upgraded);
        }
    }

    /// <summary>The lookup keys a set of demo products refers to (categories include their parents).</summary>
    private sealed record NeededLookups(
        HashSet<string> Categories, HashSet<string> Colors, HashSet<string> Materials, HashSet<string> Styles, HashSet<string> Sizes)
    {
        public static NeededLookups For(IEnumerable<ProductSeed> products)
        {
            var result = new NeededLookups(NewSet(), NewSet(), NewSet(), NewSet(), NewSet());
            foreach (var product in products)
            {
                for (var category = CatalogSeedData.Categories.FirstOrDefault(c => c.Slug == product.CategorySlug);
                     category is not null;
                     category = CatalogSeedData.Categories.FirstOrDefault(c => c.Slug == category.ParentSlug))
                {
                    result.Categories.Add(category.Slug);
                }

                result.Colors.UnionWith(product.Colors.Select(o => o.Slug).Concat(product.SecondaryColors.Select(o => o.Slug)));
                result.Materials.UnionWith(product.Materials.Select(o => o.Slug).Concat(product.SecondaryMaterials.Select(o => o.Slug)));
                result.Sizes.UnionWith(product.Sizes.Select(o => o.Slug));
                result.Styles.Add(product.StyleCode);
            }

            return result;
        }

        private static HashSet<string> NewSet() => new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Fills SearchText for products saved before the column existed (or edited outside the app).</summary>
    public async Task BackfillSearchTextAsync(CancellationToken cancellationToken = default)
    {
        if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => p.SearchText == string.Empty, cancellationToken))
        {
            return;
        }

        var products = await context.Products.IgnoreQueryFilters()
            .Where(p => p.SearchText == string.Empty)
            .Include(p => p.Category).ThenInclude(c => c.Parent)
            .Include(p => p.Style)
            .Include(p => p.Variants).ThenInclude(v => v.Materials).ThenInclude(m => m.Material)
            .Include(p => p.Variants).ThenInclude(v => v.Colors).ThenInclude(c => c.Color)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        foreach (var product in products)
        {
            product.SearchText = ProductSearchText.Build(
                product.Name, product.Sku, product.Category.Name, product.Category.Parent?.Name, product.Style?.Name,
                product.Variants.SelectMany(v => v.Materials).Select(m => m.Material.Name),
                product.Variants.SelectMany(v => v.Colors).Select(c => c.Color.Name),
                FurnitureTypes.NameOf(product.FurnitureType));
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Rebuilt search text for {Count} products", products.Count);
    }

    private sealed record Lookups(
        Dictionary<string, Category> Categories,
        Dictionary<string, ProductColor> Colors,
        Dictionary<string, ProductMaterial> Materials,
        Dictionary<string, ProductStyle> Styles,
        Dictionary<string, ProductSize> Sizes);

    private async Task<Dictionary<string, Category>> SeedCategoriesAsync(ISet<string>? include, CancellationToken ct)
    {
        var existing = await context.Categories.ToDictionaryAsync(c => c.Slug, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var seed in CatalogSeedData.Categories)
        {
            if (existing.ContainsKey(seed.Slug) || include?.Contains(seed.Slug) == false)
            {
                continue;
            }

            var category = new Category
            {
                Slug = seed.Slug,
                Name = seed.Name,
                Description = seed.Description,
                IconCssClass = seed.Icon,
                DisplayOrder = seed.Order,
                ShowOnHomePage = seed.ShowOnHome,
                IsActive = true,
                MetaTitle = $"{seed.Name} - Nội thất gỗ đẹp, giá tốt",
                MetaDescription = seed.Description,
                Parent = seed.ParentSlug is null ? null : existing[seed.ParentSlug]
            };

            existing[seed.Slug] = category;
            context.Categories.Add(category);
        }

        return existing;
    }

    private static async Task<Dictionary<string, TEntity>> SeedLookupAsync<TSeed, TEntity>(
        DbSet<TEntity> set,
        IReadOnlyList<TSeed> seeds,
        Func<TSeed, string> seedKey,
        Func<TSeed, int, TEntity> create,
        Func<TEntity, string> entityKey,
        ISet<string>? include,
        CancellationToken ct) where TEntity : class
    {
        var existing = (await set.ToListAsync(ct)).ToDictionary(entityKey, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < seeds.Count; i++)
        {
            var key = seedKey(seeds[i]);
            if (!existing.ContainsKey(key) && include?.Contains(key) != false)
            {
                var entity = create(seeds[i], i + 1);
                existing[key] = entity;
                set.Add(entity);
            }
        }

        return existing;
    }

    private static Product BuildProduct(ProductSeed seed, int index, DateTime now, Lookups lookups, HashSet<string> usedSkus, HashSet<string> usedSlugs)
    {
        var style = lookups.Styles[seed.StyleCode];
        var primaryMaterialGroup = lookups.Materials[seed.Materials[0].Slug].Group;
        var slug = SlugGenerator.Generate(seed.Name);

        Register(usedSkus, seed.Sku, "SKU");
        Register(usedSlugs, slug, "slug");

        var product = new Product
        {
            Category = lookups.Categories[seed.CategorySlug],
            Style = style,
            Name = seed.Name,
            Slug = slug,
            Sku = seed.Sku,
            ShortDescription = seed.ShortDescription,
            Description = seed.Description,
            FurnitureType = seed.Type,
            Status = ProductStatus.Active,
            IsFeatured = seed.Featured,
            SoldCount = seed.Sold,
            ViewCount = seed.Sold * 12 + 40,
            LengthMm = seed.LengthMm,
            WidthMm = seed.WidthMm,
            HeightMm = seed.HeightMm,
            WeightKg = seed.WeightKg,
            WarrantyMonths = seed.WarrantyMonths,
            Origin = Origin,
            CareInstructions = CareInstructionsFor(primaryMaterialGroup),
            MetaTitle = seed.Name,
            MetaDescription = seed.ShortDescription,
            // Spread publish dates over the last ~3 months so "Sản phẩm mới" has a meaningful order.
            PublishedAt = now.AddDays(-((index * 7) % 90) - 1)
        };

        var firstColor = lookups.Colors[seed.Colors[0].Slug];
        product.Images.Add(new ProductImage
        {
            Url = PlaceholderImages.BuildUrl(seed.Shape, firstColor.HexCode, PlaceholderImages.Backgrounds.Beige),
            AltText = seed.Name,
            DisplayOrder = 0,
            IsPrimary = true,
            CreatedAt = now
        });
        product.Images.Add(new ProductImage
        {
            Url = PlaceholderImages.BuildUrl(seed.Shape, firstColor.HexCode, PlaceholderImages.Backgrounds.Sand),
            AltText = $"{seed.Name} - góc nhìn khác",
            DisplayOrder = 1,
            CreatedAt = now
        });

        var multiMaterial = seed.Materials.Length > 1;
        OptionSeed?[] sizeOptions = seed.Sizes.Length > 0 ? [.. seed.Sizes] : [null];
        var order = 0;

        foreach (var materialOption in seed.Materials)
        foreach (var colorOption in seed.Colors)
        foreach (var sizeOption in sizeOptions)
        {
            var material = lookups.Materials[materialOption.Slug];
            var color = lookups.Colors[colorOption.Slug];
            var size = sizeOption is null ? null : lookups.Sizes[sizeOption.Slug];

            var listPrice = seed.BasePrice + materialOption.PriceDelta + colorOption.PriceDelta + (sizeOption?.PriceDelta ?? 0);
            var price = listPrice;
            decimal? originalPrice = null;
            if (seed.SalePercent > 0)
            {
                originalPrice = listPrice;
                price = RoundToTenThousand(listPrice * (100 - seed.SalePercent) / 100m);
            }

            var sku = BuildVariantSku(seed.Sku, multiMaterial ? material.Slug : null, color.Slug, size?.Slug);
            Register(usedSkus, sku, "variant SKU");

            var nameParts = new string?[] { multiMaterial ? material.Name : null, color.Name, size?.Name }.OfType<string>();
            var variant = new ProductVariant
            {
                Name = string.Join(" / ", nameParts),
                Sku = sku,
                Style = style,
                Price = price,
                OriginalPrice = originalPrice,
                StockQuantity = StockFor(sku),
                LowStockThreshold = 3,
                WeightKg = seed.WeightKg,
                IsActive = true,
                DisplayOrder = order
            };

            variant.Materials.Add(new ProductVariantMaterial { Material = material, IsPrimary = true });
            foreach (var secondary in seed.SecondaryMaterials.Where(s => s.Slug != material.Slug))
            {
                variant.Materials.Add(new ProductVariantMaterial { Material = lookups.Materials[secondary.Slug], IsPrimary = false, Part = secondary.Part });
            }

            variant.Colors.Add(new ProductVariantColor { Color = color, IsPrimary = true });
            foreach (var secondary in seed.SecondaryColors.Where(s => s.Slug != color.Slug))
            {
                variant.Colors.Add(new ProductVariantColor { Color = lookups.Colors[secondary.Slug], IsPrimary = false, Part = secondary.Part });
            }

            if (size is not null)
            {
                variant.Sizes.Add(new ProductVariantSize { Size = size, IsPrimary = true });
            }

            variant.Images.Add(new ProductImage
            {
                Product = product,
                Url = PlaceholderImages.BuildUrl(seed.Shape, color.HexCode, PlaceholderImages.Backgrounds.Cream),
                AltText = $"{seed.Name} - {variant.Name}",
                DisplayOrder = 10 + order,
                CreatedAt = now
            });

            product.Variants.Add(variant);
            order++;
        }

        // Pre-select the first variant that can actually be bought.
        var defaultVariant = product.Variants.FirstOrDefault(v => v.IsInStock) ?? product.Variants.First();
        defaultVariant.IsDefault = true;

        product.SyncFromVariants();
        var category = lookups.Categories[seed.CategorySlug];
        product.SearchText = ProductSearchText.Build(
            product.Name, product.Sku, category.Name, category.Parent?.Name, style.Name,
            product.Variants.SelectMany(v => v.Materials).Select(m => m.Material.Name),
            product.Variants.SelectMany(v => v.Colors).Select(c => c.Color.Name),
            FurnitureTypes.NameOf(product.FurnitureType));
        return product;
    }

    private static void Register(HashSet<string> used, string value, string kind)
    {
        if (!used.Add(value))
        {
            throw new InvalidOperationException($"Duplicate {kind} '{value}' in demo catalog seed data.");
        }
    }

    /// <summary>"SF-OSLO" + "xanh-reu" + "sofa-3-cho-240" → "SF-OSLO-XR-240".</summary>
    internal static string BuildVariantSku(string productSku, string? materialSlug, string colorSlug, string? sizeSlug)
    {
        var parts = new List<string> { productSku, Initials(colorSlug) };
        if (sizeSlug is not null)
        {
            parts.Add(sizeSlug[(sizeSlug.LastIndexOf('-') + 1)..].ToUpperInvariant());
        }
        if (materialSlug is not null)
        {
            parts.Add(Initials(materialSlug));
        }

        return string.Join('-', parts);
    }

    private static string Initials(string slug) =>
        string.Concat(slug.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0])));

    private static decimal RoundToTenThousand(decimal value) =>
        Math.Round(value / 10_000m, 0, MidpointRounding.AwayFromZero) * 10_000m;

    /// <summary>Deterministic pseudo-random stock (0 - 27) so demo data always has in-stock, low-stock and sold-out variants.</summary>
    internal static int StockFor(string sku)
    {
        var hash = 2166136261u;
        foreach (var c in sku)
        {
            hash = (hash ^ c) * 16777619u;
        }

        if (hash % 13 == 0) return 0;
        if (hash % 7 == 0) return 2;
        return (int)(hash % 25) + 3;
    }

    private static string CareInstructionsFor(MaterialGroup group) => group switch
    {
        MaterialGroup.NaturalWood or MaterialGroup.EngineeredWood =>
            "Lau bằng khăn mềm hơi ẩm rồi lau khô; tránh ánh nắng trực tiếp, nguồn nhiệt và hóa chất tẩy rửa mạnh. Dùng lót cốc khi đặt đồ nóng hoặc lạnh.",
        MaterialGroup.Fabric =>
            "Hút bụi định kỳ 1 - 2 lần/tuần; thấm vết bẩn bằng khăn ẩm, không chà mạnh. Vỏ đệm tháo rời nên giặt khô.",
        MaterialGroup.Leather =>
            "Lau bụi bằng khăn khô mềm; dưỡng da 3 - 6 tháng/lần; tránh ánh nắng trực tiếp và vật sắc nhọn.",
        MaterialGroup.Stone =>
            "Lau bằng khăn ẩm với dung dịch trung tính; lau khô ngay khi đổ chất có tính axit như chanh, giấm, rượu vang.",
        MaterialGroup.Rattan =>
            "Phủi bụi bằng chổi mềm; tránh để nơi ẩm ướt kéo dài; có thể lau bằng khăn ẩm rồi phơi nơi thoáng gió.",
        _ => "Vệ sinh bằng khăn mềm hơi ẩm; tránh va đập mạnh và hóa chất tẩy rửa."
    };
}
