using FurnitureStore.Domain.Entities;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Infrastructure.Persistence.Seed;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FurnitureStore.Tests.Persistence;

/// <summary>
/// The dining-set line (Russian oak, walnut color, table 1m2 + 4 chairs / 1m6 + 6 chairs) and how a database seeded by an
/// earlier version receives it.
/// </summary>
public sealed class DiningCatalogSeedTests : IDisposable
{
    private static readonly string[] DiningSets = ["BBA-ANGIA", "BBA-PHUCLOC", "BBA-MOCNHIEN", "BBA-TAMAN", "BBA-THINHGIA", "BBA-BINHMINH"];
    private static readonly string[] SetSizes = ["set-4-ghe-120", "set-6-ghe-160"];

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task SeedCatalogAsync()
    {
        await using var context = _database.CreateContext();
        await new CatalogSeeder(context, _database.TimeProvider, NullLogger<CatalogSeeder>.Instance).SeedAsync();
    }

    private async Task SeedPriceRulesAsync()
    {
        await using var context = _database.CreateContext();
        await new PriceRuleSeeder(context, NullLogger<PriceRuleSeeder>.Instance).SeedAsync();
    }

    private async Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        await using var context = _database.CreateContext();
        return await action(context);
    }

    /// <summary>Turns the database into one seeded before the dining sets, their material and their sizes existed.</summary>
    private Task RemoveDiningLineAsync() => Db(async db =>
    {
        var sets = DiningSets.ToList();
        await db.ProductVariantColors.IgnoreQueryFilters().Where(x => sets.Contains(x.ProductVariant.Product.Sku)).ExecuteDeleteAsync();
        await db.ProductVariantMaterials.IgnoreQueryFilters().Where(x => sets.Contains(x.ProductVariant.Product.Sku)).ExecuteDeleteAsync();
        await db.ProductVariantSizes.IgnoreQueryFilters().Where(x => sets.Contains(x.ProductVariant.Product.Sku)).ExecuteDeleteAsync();
        await db.ProductImages.IgnoreQueryFilters().Where(x => sets.Contains(x.Product.Sku)).ExecuteDeleteAsync();
        await db.ProductVariants.IgnoreQueryFilters().Where(x => sets.Contains(x.Product.Sku)).ExecuteDeleteAsync();
        await db.Products.IgnoreQueryFilters().Where(x => sets.Contains(x.Sku)).ExecuteDeleteAsync();
        await db.PriceRules.Where(r => r.Code == "MAT-GO-SOI-NGA").ExecuteDeleteAsync();
        await db.ProductMaterials.Where(m => m.Slug == "go-soi-nga").ExecuteDeleteAsync();
        await db.ProductSizes.Where(s => SetSizes.Contains(s.Slug)).ExecuteDeleteAsync();
        return 0;
    });

    [Fact]
    public async Task DiningSets_AreRussianOakInWalnut_WithFourAndSixChairVersions()
    {
        await SeedCatalogAsync();

        var sets = await Db(db => db.Products.Where(p => DiningSets.Contains(p.Sku))
            .Include(p => p.Category)
            .Include(p => p.Variants).ThenInclude(v => v.Materials).ThenInclude(m => m.Material)
            .Include(p => p.Variants).ThenInclude(v => v.Colors).ThenInclude(c => c.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Sizes).ThenInclude(s => s.Size)
            .AsSplitQuery()
            .ToListAsync());

        Assert.Equal(DiningSets.Length, sets.Count);
        Assert.All(sets, set =>
        {
            Assert.Equal("bo-ban-an", set.Category.Slug);
            Assert.All(set.Variants, v => Assert.Equal("Gỗ sồi Nga", v.Materials.Single(m => m.IsPrimary).Material.Name));
            Assert.Contains(set.Variants, v => v.Colors.Single(c => c.IsPrimary).Color.Name == "Nâu óc chó");
            Assert.Equal(["Bàn 1m2 + 4 ghế", "Bàn 1m6 + 6 ghế"],
                set.Variants.Select(v => v.Sizes.Single().Size.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal));

            decimal PriceOf(string size) => set.Variants.Where(v => v.Sizes.Single().Size.Slug == size).Min(v => v.OriginalPrice ?? v.Price);
            Assert.True(PriceOf("set-6-ghe-160") > PriceOf("set-4-ghe-120"), $"{set.Sku}: the 6-chair set must cost more");
        });
        Assert.Contains(sets, s => s.IsFeatured);
    }

    [Fact]
    public async Task OlderCatalog_IsToppedUp_WithTheDiningSets_TheirMaterialSizesAndQuotePrice()
    {
        await SeedCatalogAsync();
        await SeedPriceRulesAsync();
        await RemoveDiningLineAsync();
        // A color no dining set uses, deleted by an admin: topping up must not bring it back.
        await Db(db => db.ProductColors.Where(c => c.Slug == "xanh-duong-nhat").ExecuteDeleteAsync());
        var productsBefore = await Db(db => db.Products.CountAsync());

        await SeedCatalogAsync();

        Assert.Equal(productsBefore + DiningSets.Length, await Db(db => db.Products.CountAsync()));
        // 2 sizes each; Mộc Nhiên also comes in natural wood color.
        Assert.Equal(DiningSets.Length * 2 + 2, await Db(db => db.ProductVariants.CountAsync(v => DiningSets.Contains(v.Product.Sku))));
        Assert.True(await Db(db => db.ProductMaterials.AnyAsync(m => m.Slug == "go-soi-nga")));
        Assert.Equal(2, await Db(db => db.ProductSizes.CountAsync(s => SetSizes.Contains(s.Slug))));
        Assert.False(await Db(db => db.ProductColors.AnyAsync(c => c.Slug == "xanh-duong-nhat")));

        var price = await Db(db => db.PriceRules.Include(r => r.Material).SingleAsync(r => r.Code == "MAT-GO-SOI-NGA"));
        Assert.Equal("go-soi-nga", price.Material!.Slug);
        Assert.True(price.IsActive);

        // Running again changes nothing; a quote price the admin deletes stays deleted.
        await Db(db => db.PriceRules.Where(r => r.Code == "MAT-GO-SOI-NGA").ExecuteDeleteAsync());
        await SeedCatalogAsync();
        await SeedPriceRulesAsync();
        Assert.Equal(productsBefore + DiningSets.Length, await Db(db => db.Products.CountAsync()));
        Assert.False(await Db(db => db.PriceRules.AnyAsync(r => r.Code == "MAT-GO-SOI-NGA")));
    }

    [Fact]
    public async Task DemoProduct_DeletedByAnAdmin_IsNotSeededAgain()
    {
        await SeedCatalogAsync();
        await Db(async db =>
        {
            db.Products.Remove(await db.Products.SingleAsync(p => p.Sku == "BBA-TAMAN"));   // soft delete
            return await db.SaveChangesAsync();
        });

        await SeedCatalogAsync();

        Assert.False(await Db(db => db.Products.AnyAsync(p => p.Sku == "BBA-TAMAN")));
        Assert.Equal(1, await Db(db => db.Products.IgnoreQueryFilters().CountAsync(p => p.Sku == "BBA-TAMAN")));
    }

    [Fact]
    public async Task CatalogNotCreatedFromTheDemoData_IsLeftAlone()
    {
        await Db(async db =>
        {
            var category = new Category { Name = "Bàn", Slug = "ban", IsActive = true };
            db.Products.Add(new Product { Name = "Bàn riêng", Slug = "ban-rieng", Sku = "MY-001", Category = category, BasePrice = 1_000_000 });
            return await db.SaveChangesAsync();
        });

        await SeedCatalogAsync();

        Assert.Equal("MY-001", Assert.Single(await Db(db => db.Products.Select(p => p.Sku).ToListAsync())));
    }

    [Fact]
    public async Task UneditedDescriptions_PromisingFreeDelivery_AreUpgraded_EditedOnesAreKept()
    {
        await SeedCatalogAsync();
        await Db(async db =>
        {
            var family = await db.Products.SingleAsync(p => p.Sku == "BBA-FAMILY");
            // As created by the earlier seed data, through the SQL setup script (Windows line breaks).
            family.Description = "Bộ Family gồm 1 bàn ăn chữ nhật và 6 ghế tựa nan, đồng bộ màu sơn.\r\nTiết kiệm khoảng 15% so với mua lẻ, giao và lắp đặt miễn phí.";
            (await db.Products.SingleAsync(p => p.Sku == "KTV-SLIM")).Description = "Mô tả do admin viết, lắp đặt miễn phí.";
            return await db.SaveChangesAsync();
        });

        await SeedCatalogAsync();

        var descriptions = await Db(db => db.Products.Where(p => p.Sku == "BBA-FAMILY" || p.Sku == "KTV-SLIM").ToDictionaryAsync(p => p.Sku, p => p.Description));
        Assert.DoesNotContain("miễn phí", descriptions["BBA-FAMILY"]);
        Assert.Contains("giao và lắp đặt tận nơi", descriptions["BBA-FAMILY"]);
        Assert.Equal("Mô tả do admin viết, lắp đặt miễn phí.", descriptions["KTV-SLIM"]);
    }

    [Fact]
    public async Task ChatbotKnowledge_UneditedShippingAndPaymentTexts_AreUpgraded_EditedOnesAreKept()
    {
        await Db(async db =>
        {
            await new AiKnowledgeSeeder(db, NullLogger<AiKnowledgeSeeder>.Instance).SeedAsync();
            var entries = await db.AIKnowledgeEntries.ToDictionaryAsync(e => e.Title);
            entries["Giao hàng & lắp đặt"].Content = "Miễn phí giao hàng và lắp đặt cho đơn từ 10.000.000đ; đơn nhỏ hơn phí giao và lắp đặt là 300.000đ. "
                + "Nội thành thường giao trong 2 - 4 ngày, các tỉnh 3 - 7 ngày. Đồ đặt đóng theo yêu cầu cần thêm thời gian sản xuất, cửa hàng báo cụ thể khi xác nhận.";
            entries["Thanh toán"].Content = "Nội dung admin tự viết.";
            return await db.SaveChangesAsync();
        });

        await Db(async db => { await new AiKnowledgeSeeder(db, NullLogger<AiKnowledgeSeeder>.Instance).SeedAsync(); return 0; });

        var contents = await Db(db => db.AIKnowledgeEntries.ToDictionaryAsync(e => e.Title, e => e.Content));
        Assert.Equal(AiKnowledgeSeeder.ShippingContent, contents["Giao hàng & lắp đặt"]);
        Assert.Equal("Nội dung admin tự viết.", contents["Thanh toán"]);
        Assert.Equal(6, contents.Count);
    }
}
