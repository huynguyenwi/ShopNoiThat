using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Domain.Entities;

namespace FurnitureStore.Application.Catalog;

public interface ICatalogService
{
    Task<PagedResult<ProductCardDto>> GetProductsAsync(ProductQuery query, CancellationToken cancellationToken = default);
    Task<ProductDetailDto?> GetProductBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<ProductDetailDto?> GetProductByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductCardDto>> GetRelatedProductsAsync(ProductDetailDto product, int take = 8, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchSuggestionDto>> SuggestAsync(string? term, int limit = 8, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoryNodeDto>> GetCategoryTreeAsync(CancellationToken cancellationToken = default);
    Task<CategoryNodeDto?> FindCategoryAsync(string slug, CancellationToken cancellationToken = default);
    Task<FilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
    Task<HomePageDto> GetHomePageAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SitemapEntryDto>> GetSitemapEntriesAsync(CancellationToken cancellationToken = default);
    Task RecordViewAsync(int productId, CancellationToken cancellationToken = default);
}

public sealed class CatalogService(
    IProductRepository products,
    ICategoryRepository categories,
    IRepository<ProductColor> colors,
    IRepository<ProductMaterial> materials,
    IRepository<ProductSize> sizes,
    IRepository<ProductStyle> styles,
    CatalogCache cache,
    TimeProvider timeProvider) : ICatalogService
{
    /// <summary>Products published within this period get the "Mới" badge.</summary>
    public static readonly TimeSpan NewProductPeriod = TimeSpan.FromDays(30);

    private DateTime NewSince => timeProvider.GetUtcNow().UtcDateTime - NewProductPeriod;

    public async Task<PagedResult<ProductCardDto>> GetProductsAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        query.Normalize();

        IReadOnlyCollection<int>? categoryIds = null;
        if (!string.IsNullOrWhiteSpace(query.CategorySlug))
        {
            var category = await FindCategoryAsync(query.CategorySlug, cancellationToken);
            if (category is null)
            {
                return PagedResult<ProductCardDto>.Empty(query.Page, query.PageSize);
            }

            categoryIds = Flatten(category).Select(c => c.Id).ToList();
        }

        return await products.SearchAsync(query, categoryIds, NewSince, cancellationToken);
    }

    /// <summary>Cached like the other catalog data: admin changes invalidate it immediately.</summary>
    public Task<IReadOnlyList<SitemapEntryDto>> GetSitemapEntriesAsync(CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync("sitemap-products", () => products.GetSitemapEntriesAsync(cancellationToken));

    public async Task<ProductDetailDto?> GetProductBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 220)
        {
            return null;
        }

        var product = await products.GetDetailBySlugAsync(slug.Trim().ToLowerInvariant(), cancellationToken);
        return product is null || !product.IsVisibleToCustomers ? null : CatalogMapper.ToDetail(product);
    }

    public async Task<ProductDetailDto?> GetProductByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await products.GetDetailByIdAsync(id, cancellationToken);
        return product is null || !product.IsVisibleToCustomers ? null : CatalogMapper.ToDetail(product);
    }

    public Task<IReadOnlyList<ProductCardDto>> GetRelatedProductsAsync(ProductDetailDto product, int take = 8, CancellationToken cancellationToken = default) =>
        products.GetRelatedAsync(product.Id, product.Category.Id, take, NewSince, cancellationToken);

    public async Task<IReadOnlyList<SearchSuggestionDto>> SuggestAsync(string? term, int limit = 8, CancellationToken cancellationToken = default)
    {
        var terms = ProductSearchText.Terms(term);
        if (terms.Count == 0 || terms.Sum(t => t.Length) < 2)
        {
            return [];
        }

        return await products.SuggestAsync(terms, Math.Clamp(limit, 1, 20), cancellationToken);
    }

    public Task<IReadOnlyList<CategoryNodeDto>> GetCategoryTreeAsync(CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync("catalog:category-tree", async () =>
        {
            var all = await categories.GetAllOrderedAsync(cancellationToken);
            var counts = await categories.GetActiveProductCountsAsync(cancellationToken);
            var active = all.Where(c => c.IsActive).ToList();

            CategoryNodeDto Build(Category category)
            {
                var children = active.Where(c => c.ParentId == category.Id).Select(Build).ToList();
                var own = counts.GetValueOrDefault(category.Id);
                return new CategoryNodeDto(category.Id, category.Name, category.Slug, category.Description, category.IconCssClass,
                    own + children.Sum(c => c.ProductCount), children);
            }

            return (IReadOnlyList<CategoryNodeDto>)active.Where(c => c.ParentId is null).Select(Build).ToList();
        });

    public async Task<CategoryNodeDto?> FindCategoryAsync(string slug, CancellationToken cancellationToken = default)
    {
        var tree = await GetCategoryTreeAsync(cancellationToken);
        return tree.SelectMany(Flatten).FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    public Task<FilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync("catalog:filter-options", async () =>
        {
            var tree = await GetCategoryTreeAsync(cancellationToken);
            var colorList = await colors.ListAsync(c => c.IsActive, cancellationToken);
            var materialList = await materials.ListAsync(m => m.IsActive, cancellationToken);
            var sizeList = await sizes.ListAsync(s => s.IsActive, cancellationToken);
            var styleList = await styles.ListAsync(s => s.IsActive, cancellationToken);
            var (min, max) = await products.GetPriceRangeAsync(cancellationToken);

            return new FilterOptionsDto(
                tree,
                colorList.OrderBy(c => c.DisplayOrder).Select(c => new OptionDto(c.Id, c.Name, c.Slug, c.HexCode)).ToList(),
                materialList.OrderBy(m => m.DisplayOrder).Select(m => new OptionDto(m.Id, m.Name, m.Slug)).ToList(),
                sizeList.OrderBy(s => s.DisplayOrder).Select(s => new OptionDto(s.Id, s.Name, s.Slug, Description: s.DimensionsText)).ToList(),
                styleList.OrderBy(s => s.DisplayOrder).Select(s => new OptionDto(s.Id, s.Name, s.Slug)).ToList(),
                min, max);
        });

    public Task<HomePageDto> GetHomePageAsync(CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync("catalog:home", async () =>
        {
            async Task<IReadOnlyList<ProductCardDto>> Take(ProductQuery query, IReadOnlyCollection<int>? categoryIds = null) =>
                (await products.SearchAsync(query.Normalize(), categoryIds, NewSince, cancellationToken)).Items;

            var featured = await Take(new ProductQuery { FeaturedOnly = true, Sort = ProductSort.BestSelling, PageSize = 8 });
            var newest = await Take(new ProductQuery { Sort = ProductSort.Newest, PageSize = 8 });
            var bestSellers = await Take(new ProductQuery { Sort = ProductSort.BestSelling, PageSize = 8 });
            var onSale = await Take(new ProductQuery { OnSale = true, Sort = ProductSort.BestSelling, PageSize = 8 });

            var rooms = new List<RoomSectionDto>();
            foreach (var room in (await GetCategoryTreeAsync(cancellationToken)).Where(c => c.Children.Count > 0).Take(4))
            {
                var items = await Take(new ProductQuery { Sort = ProductSort.BestSelling, PageSize = 4 }, Flatten(room).Select(c => c.Id).ToList());
                if (items.Count > 0)
                {
                    rooms.Add(new RoomSectionDto(room, items));
                }
            }

            return new HomePageDto(featured, newest, bestSellers, onSale, rooms);
        });

    public Task RecordViewAsync(int productId, CancellationToken cancellationToken = default) =>
        products.IncrementViewCountAsync(productId, cancellationToken);

    private static IEnumerable<CategoryNodeDto> Flatten(CategoryNodeDto node) =>
        new[] { node }.Concat(node.Children.SelectMany(Flatten));
}
