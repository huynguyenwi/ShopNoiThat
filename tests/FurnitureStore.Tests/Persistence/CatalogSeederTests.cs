using FurnitureStore.Infrastructure.Persistence.Seed;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FurnitureStore.Tests.Persistence;

public sealed class CatalogSeederTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task SeedAsync()
    {
        await using var context = _database.CreateContext();
        await new CatalogSeeder(context, _database.TimeProvider, NullLogger<CatalogSeeder>.Instance).SeedAsync();
    }

    [Fact]
    public async Task Seed_CreatesRequiredDemoVolume()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();

        Assert.True(await context.Categories.CountAsync() >= 10);
        Assert.True(await context.Products.CountAsync() >= 30);
        Assert.True(await context.ProductVariants.CountAsync() >= 60);
        Assert.True(await context.ProductColors.CountAsync() >= 10);
        Assert.True(await context.ProductMaterials.CountAsync() >= 10);
        Assert.Equal(8, await context.ProductStyles.CountAsync());
        Assert.True(await context.ProductSizes.CountAsync() >= 10);
    }

    [Fact]
    public async Task Seed_IsIdempotent()
    {
        await SeedAsync();
        await SeedAsync();
        await using var context = _database.CreateContext();

        Assert.Equal(CatalogSeedData.Products.Length, await context.Products.CountAsync());
        Assert.Equal(CatalogSeedData.Categories.Length, await context.Categories.CountAsync());
    }

    [Fact]
    public async Task EveryProduct_HasVariants_ImagesAndExactlyOneDefaultVariant()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();

        var products = await context.Products
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .AsSplitQuery()
            .ToListAsync();

        foreach (var product in products)
        {
            Assert.NotEmpty(product.Variants);
            Assert.Single(product.Variants, v => v.IsDefault);
            Assert.Single(product.Images, i => i.IsPrimary);

            // The pre-selected variant must be buyable whenever at least one variant is in stock.
            if (product.Variants.Any(v => v.IsInStock))
            {
                Assert.True(product.Variants.Single(v => v.IsDefault).IsInStock, $"{product.Sku}: default variant is out of stock");
            }
        }
    }

    [Fact]
    public async Task VariantSkusAndProductSlugs_AreUnique()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();

        var skus = await context.ProductVariants.Select(v => v.Sku).ToListAsync();
        var slugs = await context.Products.Select(p => p.Slug).ToListAsync();

        Assert.Equal(skus.Count, skus.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(slugs.Count, slugs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(slugs, slug => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", slug));
    }

    [Fact]
    public async Task ProductListPriceAndStock_AreSyncedFromVariants()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();

        var products = await context.Products.Include(p => p.Variants).ToListAsync();

        foreach (var product in products)
        {
            var active = product.Variants.Where(v => v.IsActive).ToList();
            Assert.Equal(active.Sum(v => v.StockQuantity), product.StockQuantity);
            Assert.Equal(active.Min(v => v.Price), product.EffectivePrice);
        }
    }

    [Fact]
    public async Task EachVariant_HasExactlyOnePrimaryColorAndMaterial()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();

        var variants = await context.ProductVariants
            .Include(v => v.Colors)
            .Include(v => v.Materials)
            .Include(v => v.Sizes)
            .AsSplitQuery()
            .ToListAsync();

        Assert.All(variants, v =>
        {
            Assert.Single(v.Colors, c => c.IsPrimary);
            Assert.Single(v.Materials, m => m.IsPrimary);
            Assert.True(v.Sizes.Count(s => s.IsPrimary) <= 1);
        });

        // Multi-color / multi-material variants exist (e.g. walnut desk with black steel frame).
        Assert.Contains(variants, v => v.Colors.Count > 1);
        Assert.Contains(variants, v => v.Materials.Count > 1);
    }

    [Fact]
    public async Task DemoData_CoversSaleLowStockAndOutOfStockCases()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();

        Assert.True(await context.ProductVariants.AnyAsync(v => v.OriginalPrice != null));
        Assert.True(await context.ProductVariants.AnyAsync(v => v.StockQuantity == 0));
        Assert.True(await context.ProductVariants.AnyAsync(v => v.StockQuantity > 0 && v.StockQuantity <= v.LowStockThreshold));
    }

    [Theory]
    [InlineData("SF-OSLO", null, "xanh-reu", "sofa-3-cho-240", "SF-OSLO-XR-240")]
    [InlineData("GA-CURVE", null, "soi-tu-nhien", null, "GA-CURVE-STN")]
    [InlineData("X", "go-oc-cho", "den", "ban-an-180", "X-D-180-GOC")]
    public void BuildVariantSku_IsReadableAndDeterministic(string productSku, string? material, string color, string? size, string expected)
    {
        Assert.Equal(expected, CatalogSeeder.BuildVariantSku(productSku, material, color, size));
    }
}
