using FurnitureStore.Application.Catalog.Admin;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Domain.Entities;

namespace FurnitureStore.Application.Catalog;

public interface IProductRepository : IRepository<Product>
{
    /// <summary>Public listing: only active products, projected to cards.</summary>
    Task<PagedResult<ProductCardDto>> SearchAsync(ProductQuery query, IReadOnlyCollection<int>? categoryIds, DateTime newSinceUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchSuggestionDto>> SuggestAsync(IReadOnlyList<string> terms, int limit, CancellationToken cancellationToken = default);

    /// <summary>Full read-only graph (category, style, images, variants with colors/materials/sizes).</summary>
    Task<Product?> GetDetailBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<Product?> GetDetailByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Tracked graph for editing (variants, junction rows, images).</summary>
    Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductCardDto>> GetRelatedAsync(int productId, int categoryId, int take, DateTime newSinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Active products for sitemap.xml: slug and last modification (UTC).</summary>
    Task<IReadOnlyList<SitemapEntryDto>> GetSitemapEntriesAsync(CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, int? excludeProductId, CancellationToken cancellationToken = default);

    Task<bool> SkuExistsAsync(string sku, int? excludeProductId, CancellationToken cancellationToken = default);

    /// <summary>Returns the variant SKUs from <paramref name="skus"/> that are already used by other products.</summary>
    Task<IReadOnlyList<string>> FindVariantSkusInUseAsync(IReadOnlyCollection<string> skus, int? excludeProductId, CancellationToken cancellationToken = default);

    Task<(decimal Min, decimal Max)> GetPriceRangeAsync(CancellationToken cancellationToken = default);

    /// <summary>Sizes offered by the active products of these categories, with the number of products offering each (most offered first).</summary>
    Task<IReadOnlyList<SizeInUseDto>> GetSizesInUseAsync(IReadOnlyCollection<int> categoryIds, CancellationToken cancellationToken = default);

    Task IncrementViewCountAsync(int productId, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminProductListItemDto>> SearchAdminAsync(AdminProductQuery query, CancellationToken cancellationToken = default);
}

public interface ICategoryRepository : IRepository<Category>
{
    Task<IReadOnlyList<Category>> GetAllOrderedAsync(CancellationToken cancellationToken = default);

    /// <summary>Number of active products directly in each category.</summary>
    Task<IReadOnlyDictionary<int, int>> GetActiveProductCountsAsync(CancellationToken cancellationToken = default);

    Task<int> CountProductsAsync(int categoryId, CancellationToken cancellationToken = default);
}
