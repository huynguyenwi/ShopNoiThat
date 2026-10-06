using FurnitureStore.Application.Catalog;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Catalog;

public sealed class CatalogServiceTests : IAsyncLifetime
{
    private ServiceTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Catalog<T>(Func<ICatalogService, Task<T>> action) =>
        _host.RunAsync(sp => action(sp.GetRequiredService<ICatalogService>()));

    [Theory]
    [InlineData("bàn gỗ")]
    [InlineData("ban go")]
    [InlineData("BAN GO")]
    public async Task Search_IsAccentAndCaseInsensitive(string query)
    {
        var result = await Catalog(c => c.GetProductsAsync(new ProductQuery { Search = query, PageSize = 48 }));

        Assert.NotEmpty(result.Items);
        Assert.Contains(result.Items, p => p.Name == "Bàn ăn gỗ óc chó mặt liền Walnut");
        Assert.All(result.Items, p => Assert.Contains("ban", ProductSearchText.Normalize(p.Name + " " + p.CategoryName)));
    }

    [Fact]
    public async Task Search_MatchesSkuMaterialAndColor()
    {
        var bySku = await Catalog(c => c.GetProductsAsync(new ProductQuery { Search = "SF-OSLO" }));
        var byMaterial = await Catalog(c => c.GetProductsAsync(new ProductQuery { Search = "óc chó", PageSize = 48 }));
        var byColor = await Catalog(c => c.GetProductsAsync(new ProductQuery { Search = "xanh navy", PageSize = 48 }));

        Assert.Single(bySku.Items);
        Assert.True(byMaterial.TotalCount >= 5);
        Assert.Contains(byColor.Items, p => p.Name.Contains("Velvet"));
    }

    [Fact]
    public async Task RoomCategory_IncludesProductsOfSubCategories()
    {
        var room = await Catalog(c => c.GetProductsAsync(new ProductQuery { CategorySlug = "phong-an", PageSize = 48 }));
        var diningTables = await Catalog(c => c.GetProductsAsync(new ProductQuery { CategorySlug = "ban-an", PageSize = 48 }));

        Assert.True(room.TotalCount > diningTables.TotalCount);
        Assert.All(diningTables.Items, p => Assert.Equal("ban-an", p.CategorySlug));
        Assert.Contains(room.Items, p => p.CategorySlug == "ghe-an");
    }

    [Fact]
    public async Task UnknownCategory_ReturnsEmptyPage()
    {
        var result = await Catalog(c => c.GetProductsAsync(new ProductQuery { CategorySlug = "khong-ton-tai" }));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Filters_ByColorMaterialStyleAndType()
    {
        var black = await Catalog(c => c.GetProductsAsync(new ProductQuery { ColorSlugs = ["den"], PageSize = 48 }));
        var oak = await Catalog(c => c.GetProductsAsync(new ProductQuery { MaterialSlugs = ["go-soi"], PageSize = 48 }));
        var japandi = await Catalog(c => c.GetProductsAsync(new ProductQuery { StyleSlugs = ["japandi"], PageSize = 48 }));
        var chairs = await Catalog(c => c.GetProductsAsync(new ProductQuery { FurnitureType = FurnitureType.Chair, PageSize = 48 }));

        Assert.Contains(black.Items, p => p.Sku == "GA-CURVE");
        Assert.DoesNotContain(black.Items, p => p.Sku == "BT-LUNA");
        Assert.Contains(oak.Items, p => p.Sku == "SF-OSLO"); // oak is the frame (secondary material)
        Assert.All(japandi.Items, p => Assert.Equal("Japandi", p.StyleName));
        Assert.True(chairs.TotalCount >= 5);
    }

    [Fact]
    public async Task PriceRange_OnSale_AndInStock_Filters()
    {
        var cheap = await Catalog(c => c.GetProductsAsync(new ProductQuery { MaxPrice = 3_000_000, PageSize = 48 }));
        var mid = await Catalog(c => c.GetProductsAsync(new ProductQuery { MinPrice = 10_000_000, MaxPrice = 20_000_000, PageSize = 48 }));
        var sale = await Catalog(c => c.GetProductsAsync(new ProductQuery { OnSale = true, PageSize = 48 }));
        var inStock = await Catalog(c => c.GetProductsAsync(new ProductQuery { InStock = true, PageSize = 48 }));

        Assert.All(cheap.Items, p => Assert.True(p.Price <= 3_000_000));
        Assert.All(mid.Items, p => Assert.InRange(p.Price, 10_000_000m, 20_000_000m));
        Assert.NotEmpty(sale.Items);
        Assert.All(sale.Items, p => Assert.True(p.DiscountPercent > 0 && p.OriginalPrice > p.Price));
        Assert.All(inStock.Items, p => Assert.True(p.InStock));
    }

    [Fact]
    public async Task MinRating_ExcludesProductsWithoutReviews()
    {
        var result = await Catalog(c => c.GetProductsAsync(new ProductQuery { MinRating = 4 }));

        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData(ProductSort.PriceAsc)]
    [InlineData(ProductSort.PriceDesc)]
    [InlineData(ProductSort.BestSelling)]
    public async Task Sorting_IsApplied(ProductSort sort)
    {
        var items = (await Catalog(c => c.GetProductsAsync(new ProductQuery { Sort = sort, PageSize = 48 }))).Items;

        var expected = sort switch
        {
            ProductSort.PriceAsc => items.OrderBy(p => p.Price).Select(p => p.Price),
            ProductSort.PriceDesc => items.OrderByDescending(p => p.Price).Select(p => p.Price),
            _ => items.OrderByDescending(p => p.SoldCount).Select(p => (decimal)p.SoldCount)
        };
        var actual = sort == ProductSort.BestSelling ? items.Select(p => (decimal)p.SoldCount) : items.Select(p => p.Price);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Paging_ReturnsRequestedPageAndTotals()
    {
        var page1 = await Catalog(c => c.GetProductsAsync(new ProductQuery { PageSize = 10, Page = 1 }));
        var page4 = await Catalog(c => c.GetProductsAsync(new ProductQuery { PageSize = 10, Page = 4 }));

        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(37, page1.TotalCount);
        Assert.Equal(4, page1.TotalPages);
        Assert.Equal(7, page4.Items.Count);
        Assert.Empty(page1.Items.Select(p => p.Id).Intersect(page4.Items.Select(p => p.Id)));
    }

    [Fact]
    public async Task Query_ClampsAbusivePageSize()
    {
        var result = await Catalog(c => c.GetProductsAsync(new ProductQuery { PageSize = 100_000, Page = -5 }));

        Assert.Equal(ProductQuery.MaxPageSize, result.PageSize);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task Detail_MapsVariantsOptionsAndParts()
    {
        var product = await Catalog(c => c.GetProductBySlugAsync("ban-nang-ha-dien-mat-go-oc-cho"));

        Assert.NotNull(product);
        Assert.Equal("BLV-LIFT", product.Sku);
        Assert.Equal(2, product.Variants.Count);
        Assert.Equal(2, product.SizeOptions.Count);
        Assert.Single(product.ColorOptions);
        Assert.Single(product.Variants, v => v.IsDefault);
        Assert.All(product.Variants, v => Assert.Contains(v.Parts, part => part.Name == "Đen" && part.Part == "Khung nâng hạ"));
        Assert.All(product.Variants, v => Assert.NotEmpty(v.ImageUrls));
        Assert.NotNull(product.PrimaryImageUrl);
        Assert.Equal("Phòng làm việc", product.ParentCategory?.Name);
    }

    [Fact]
    public async Task Detail_OfDraftOrDeletedProduct_IsHidden()
    {
        await _host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.SingleAsync(p => p.Sku == "SF-OSLO");
            product.Status = ProductStatus.Draft;
            var other = await db.Products.SingleAsync(p => p.Sku == "SF-TOKYO");
            db.Products.Remove(other);
            await db.SaveChangesAsync();
        });

        Assert.Null(await Catalog(c => c.GetProductBySlugAsync("sofa-bang-3-cho-oslo-khung-go-soi")));
        Assert.Null(await Catalog(c => c.GetProductBySlugAsync("sofa-giuong-thong-minh-tokyo")));
        var list = await Catalog(c => c.GetProductsAsync(new ProductQuery { Search = "sofa", PageSize = 48 }));
        Assert.DoesNotContain(list.Items, p => p.Sku is "SF-OSLO" or "SF-TOKYO");
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("không-tồn-tại-🙂")]
    public async Task Detail_WithOddSlugs_ReturnsNull(string slug)
    {
        Assert.Null(await Catalog(c => c.GetProductBySlugAsync(slug)));
    }

    [Fact]
    public async Task Suggestions_ForAutocomplete()
    {
        var suggestions = await Catalog(c => c.SuggestAsync("bàn gỗ", 5));
        var tooShort = await Catalog(c => c.SuggestAsync("b", 5));

        Assert.InRange(suggestions.Count, 1, 5);
        Assert.All(suggestions, s => Assert.StartsWith("/products/", s.Url));
        Assert.Empty(tooShort);
    }

    [Fact]
    public async Task CategoryTree_HasRoomsWithChildrenAndCounts()
    {
        var tree = await Catalog(c => c.GetCategoryTreeAsync());

        Assert.Equal(5, tree.Count);
        var livingRoom = tree.Single(c => c.Slug == "phong-khach");
        Assert.Equal(4, livingRoom.Children.Count);
        Assert.Equal(livingRoom.Children.Sum(c => c.ProductCount), livingRoom.ProductCount);
    }

    [Fact]
    public async Task HomePage_HasAllSections()
    {
        var home = await Catalog(c => c.GetHomePageAsync());

        Assert.NotEmpty(home.Featured);
        Assert.All(home.Featured, p => Assert.True(p.IsFeatured));
        Assert.Equal(8, home.NewArrivals.Count);
        Assert.NotEmpty(home.OnSale);
        Assert.Equal(4, home.Rooms.Count);
        Assert.All(home.Rooms, r => Assert.InRange(r.Products.Count, 1, 4));
    }

    [Fact]
    public async Task RecordView_IncrementsViewCount()
    {
        var id = await _host.RunAsync(async sp => (await sp.GetRequiredService<ApplicationDbContext>().Products.FirstAsync()).Id);
        var before = await _host.RunAsync(async sp => (await sp.GetRequiredService<ApplicationDbContext>().Products.FindAsync(id))!.ViewCount);

        await Catalog(async c => { await c.RecordViewAsync(id); return 0; });

        var after = await _host.RunAsync(async sp => (await sp.GetRequiredService<ApplicationDbContext>().Products.AsNoTracking().SingleAsync(p => p.Id == id)).ViewCount);
        Assert.Equal(before + 1, after);
    }
}
