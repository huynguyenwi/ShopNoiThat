using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Catalog.Admin;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStore.Infrastructure.Persistence.Repositories;

public sealed class ProductRepository(ApplicationDbContext context) : EfRepository<Product>(context), IProductRepository
{
    public async Task<PagedResult<ProductCardDto>> SearchAsync(ProductQuery query, IReadOnlyCollection<int>? categoryIds, DateTime newSinceUtc, CancellationToken cancellationToken = default)
    {
        var products = Context.Products.AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active && p.Category.IsActive);

        foreach (var term in ProductSearchText.Terms(query.Search))
        {
            products = products.Where(p => p.SearchText.Contains(term));
        }

        if (categoryIds is not null)
        {
            products = products.Where(p => categoryIds.Contains(p.CategoryId));
        }

        if (query.ProductIds is not null)
        {
            var ids = query.ProductIds.ToList();
            products = products.Where(p => ids.Contains(p.Id));
        }

        if (query.FurnitureType.HasValue)
        {
            products = products.Where(p => p.FurnitureType == query.FurnitureType);
        }

        if (query.MinPrice.HasValue)
        {
            products = products.Where(p => (p.DiscountPrice ?? p.BasePrice) >= query.MinPrice.Value);
        }

        if (query.MaxPrice.HasValue)
        {
            products = products.Where(p => (p.DiscountPrice ?? p.BasePrice) <= query.MaxPrice.Value);
        }

        if (query.ColorSlugs.Count > 0)
        {
            var slugs = query.ColorSlugs.ToList();
            products = products.Where(p => p.Variants.Any(v => v.IsActive && v.Colors.Any(c => c.IsPrimary && slugs.Contains(c.Color.Slug))));
        }

        if (query.MaterialSlugs.Count > 0)
        {
            var slugs = query.MaterialSlugs.ToList();
            products = products.Where(p => p.Variants.Any(v => v.IsActive && v.Materials.Any(m => slugs.Contains(m.Material.Slug))));
        }

        if (query.SizeSlugs.Count > 0)
        {
            var slugs = query.SizeSlugs.ToList();
            products = products.Where(p => p.Variants.Any(v => v.IsActive && v.Sizes.Any(s => slugs.Contains(s.Size.Slug))));
        }

        if (query.StyleSlugs.Count > 0)
        {
            var slugs = query.StyleSlugs.ToList();
            products = products.Where(p => (p.Style != null && slugs.Contains(p.Style.Slug))
                                           || p.Variants.Any(v => v.IsActive && v.Style != null && slugs.Contains(v.Style.Slug)));
        }

        if (query.MinRating.HasValue)
        {
            products = products.Where(p => p.AverageRating >= query.MinRating.Value);
        }

        if (query.OnSale)
        {
            products = products.Where(p => p.DiscountPrice != null && p.DiscountPrice < p.BasePrice);
        }

        if (query.InStock)
        {
            products = products.Where(p => p.StockQuantity > 0);
        }

        if (query.FeaturedOnly)
        {
            products = products.Where(p => p.IsFeatured);
        }

        products = query.Sort switch
        {
            ProductSort.PriceAsc => products.OrderBy(p => p.DiscountPrice ?? p.BasePrice).ThenBy(p => p.Id),
            ProductSort.PriceDesc => products.OrderByDescending(p => p.DiscountPrice ?? p.BasePrice).ThenBy(p => p.Id),
            ProductSort.BestSelling => products.OrderByDescending(p => p.SoldCount).ThenByDescending(p => p.Id),
            ProductSort.Rating => products.OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.ReviewCount).ThenByDescending(p => p.Id),
            _ => products.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id)
        };

        var total = await products.CountAsync(cancellationToken);
        var page = await ProjectCards(products.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize), newSinceUtc, cancellationToken);
        return new PagedResult<ProductCardDto>(page, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<SearchSuggestionDto>> SuggestAsync(IReadOnlyList<string> terms, int limit, CancellationToken cancellationToken = default)
    {
        var products = Context.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active && p.Category.IsActive);
        foreach (var term in terms)
        {
            products = products.Where(p => p.SearchText.Contains(term));
        }

        var rows = await products
            .OrderByDescending(p => p.SoldCount)
            .Take(limit)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                Image = p.Images.Where(i => i.ProductVariantId == null).OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(),
                Price = p.DiscountPrice ?? p.BasePrice,
                Category = p.Category.Name
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new SearchSuggestionDto(r.Id, r.Name, r.Slug, r.Image, r.Price, r.Category)).ToList();
    }

    public Task<Product?> GetDetailBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        DetailQuery().FirstOrDefaultAsync(p => p.Slug == slug, cancellationToken);

    public Task<Product?> GetDetailByIdAsync(int id, CancellationToken cancellationToken = default) =>
        DetailQuery().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default) =>
        Context.Products
            .Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.Colors)
            .Include(p => p.Variants).ThenInclude(v => v.Materials)
            .Include(p => p.Variants).ThenInclude(v => v.Sizes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SitemapEntryDto>> GetSitemapEntriesAsync(CancellationToken cancellationToken = default) =>
        await Context.Products.AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active && p.Category.IsActive)
            .OrderBy(p => p.Id)
            .Select(p => new SitemapEntryDto(p.Slug, p.UpdatedAt ?? p.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductCardDto>> GetRelatedAsync(int productId, int categoryId, int take, DateTime newSinceUtc, CancellationToken cancellationToken = default)
    {
        var products = Context.Products.AsNoTracking()
            .Where(p => p.Id != productId && p.Status == ProductStatus.Active && p.Category.IsActive)
            .Where(p => p.CategoryId == categoryId || p.Category.ParentId == Context.Categories.Where(c => c.Id == categoryId).Select(c => c.ParentId).FirstOrDefault())
            .OrderByDescending(p => p.CategoryId == categoryId)
            .ThenByDescending(p => p.SoldCount)
            .Take(take);

        return await ProjectCards(products, newSinceUtc, cancellationToken);
    }

    public Task<bool> SlugExistsAsync(string slug, int? excludeProductId, CancellationToken cancellationToken = default) =>
        Context.Products.AnyAsync(p => p.Slug == slug && p.Id != excludeProductId, cancellationToken);

    public Task<bool> SkuExistsAsync(string sku, int? excludeProductId, CancellationToken cancellationToken = default) =>
        Context.Products.AnyAsync(p => p.Sku == sku && p.Id != excludeProductId, cancellationToken);

    public async Task<IReadOnlyList<string>> FindVariantSkusInUseAsync(IReadOnlyCollection<string> skus, int? excludeProductId, CancellationToken cancellationToken = default)
    {
        var list = skus.ToList();
        // Variants of soft-deleted products still own their SKU (unique index), so the query filter is ignored here.
        return await Context.ProductVariants.IgnoreQueryFilters()
            .Where(v => list.Contains(v.Sku) && v.ProductId != excludeProductId)
            .Select(v => v.Sku)
            .ToListAsync(cancellationToken);
    }

    public async Task<(decimal Min, decimal Max)> GetPriceRangeAsync(CancellationToken cancellationToken = default)
    {
        var prices = Context.Products.Where(p => p.Status == ProductStatus.Active).Select(p => p.DiscountPrice ?? p.BasePrice);
        if (!await prices.AnyAsync(cancellationToken))
        {
            return (0, 0);
        }

        return (await prices.MinAsync(cancellationToken), await prices.MaxAsync(cancellationToken));
    }

    public Task IncrementViewCountAsync(int productId, CancellationToken cancellationToken = default) =>
        Context.Products.Where(p => p.Id == productId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.ViewCount, p => p.ViewCount + 1), cancellationToken);

    public async Task<PagedResult<AdminProductListItemDto>> SearchAdminAsync(AdminProductQuery query, CancellationToken cancellationToken = default)
    {
        var products = Context.Products.AsNoTracking();

        foreach (var term in ProductSearchText.Terms(query.Search))
        {
            products = products.Where(p => p.SearchText.Contains(term) || p.Variants.Any(v => v.Sku.ToLower().Contains(term)));
        }

        if (query.CategoryId.HasValue)
        {
            products = products.Where(p => p.CategoryId == query.CategoryId || p.Category.ParentId == query.CategoryId);
        }

        if (query.Status.HasValue)
        {
            products = products.Where(p => p.Status == query.Status);
        }

        products = query.Stock switch
        {
            StockFilter.InStock => products.Where(p => p.StockQuantity > 0),
            StockFilter.OutOfStock => products.Where(p => p.Variants.Any(v => v.IsActive && v.StockQuantity == 0)),
            StockFilter.LowStock => products.Where(p => p.Variants.Any(v => v.IsActive && v.StockQuantity > 0 && v.StockQuantity <= v.LowStockThreshold)),
            _ => products
        };

        var total = await products.CountAsync(cancellationToken);
        var rows = await products
            .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new AdminProductListItemDto(
                p.Id, p.Name, p.Slug, p.Sku,
                p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(),
                p.Category.Name, p.Status, p.IsFeatured,
                p.DiscountPrice ?? p.BasePrice,
                p.DiscountPrice != null ? p.BasePrice : null,
                p.StockQuantity,
                p.Variants.Count,
                p.Variants.Count(v => v.IsActive && v.StockQuantity <= v.LowStockThreshold),
                p.SoldCount, p.UpdatedAt, p.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminProductListItemDto>(rows, total, query.Page, query.PageSize);
    }

    private IQueryable<Product> DetailQuery() =>
        Context.Products.AsNoTracking()
            .Include(p => p.Category).ThenInclude(c => c.Parent)
            .Include(p => p.Style)
            .Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.Style)
            .Include(p => p.Variants).ThenInclude(v => v.Colors).ThenInclude(c => c.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Materials).ThenInclude(m => m.Material)
            .Include(p => p.Variants).ThenInclude(v => v.Sizes).ThenInclude(s => s.Size)
            .AsSplitQuery();

    /// <summary>Projects a page of products to cards; swatches are loaded with a second query (portable across providers).</summary>
    private async Task<IReadOnlyList<ProductCardDto>> ProjectCards(IQueryable<Product> page, DateTime newSinceUtc, CancellationToken cancellationToken)
    {
        var rows = await page.Select(p => new
        {
            p.Id,
            p.Name,
            p.Slug,
            p.Sku,
            CategoryName = p.Category.Name,
            CategorySlug = p.Category.Slug,
            Image = p.Images.Where(i => i.ProductVariantId == null).OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(),
            p.BasePrice,
            p.DiscountPrice,
            p.AverageRating,
            p.ReviewCount,
            p.SoldCount,
            p.StockQuantity,
            p.PublishedAt,
            p.IsFeatured,
            StyleName = p.Style != null ? p.Style.Name : null
        }).ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var swatches = (await Context.ProductVariantColors.AsNoTracking()
                .Where(c => c.IsPrimary && c.ProductVariant.IsActive && ids.Contains(c.ProductVariant.ProductId))
                .Select(c => new { c.ProductVariant.ProductId, c.Color.HexCode, c.Color.DisplayOrder })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.OrderBy(x => x.DisplayOrder).Select(x => x.HexCode).Distinct().Take(5).ToList());

        return rows.Select(r =>
        {
            var onSale = r.DiscountPrice.HasValue && r.DiscountPrice < r.BasePrice;
            var price = onSale ? r.DiscountPrice!.Value : r.BasePrice;
            var discount = onSale && r.BasePrice > 0 ? (int)Math.Round((r.BasePrice - price) / r.BasePrice * 100m, MidpointRounding.AwayFromZero) : 0;
            return new ProductCardDto(
                r.Id, r.Name, r.Slug, r.Sku, r.CategoryName, r.CategorySlug, r.Image,
                price, onSale ? r.BasePrice : null, discount, r.AverageRating, r.ReviewCount, r.SoldCount,
                r.StockQuantity > 0, r.PublishedAt >= newSinceUtc, r.IsFeatured, r.StyleName,
                swatches.GetValueOrDefault(r.Id) ?? []);
        }).ToList();
    }
}

public sealed class CategoryRepository(ApplicationDbContext context) : EfRepository<Category>(context), ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> GetAllOrderedAsync(CancellationToken cancellationToken = default) =>
        await Context.Categories.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<int, int>> GetActiveProductCountsAsync(CancellationToken cancellationToken = default) =>
        await Context.Products.AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active)
            .GroupBy(p => p.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

    public Task<int> CountProductsAsync(int categoryId, CancellationToken cancellationToken = default) =>
        Context.Products.CountAsync(p => p.CategoryId == categoryId, cancellationToken);
}

public sealed class CatalogAdminRepository(ApplicationDbContext context) : ICatalogAdminRepository
{
    public async Task<IReadOnlyDictionary<int, int>> GetAttributeUsageAsync(AttributeKind kind, CancellationToken cancellationToken = default)
    {
        IEnumerable<(int Id, int Count)> rows = kind switch
        {
            AttributeKind.Color => (await context.ProductVariantColors.IgnoreQueryFilters().GroupBy(x => x.ColorId)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).Select(x => (x.Key, x.Count)),
            AttributeKind.Material => (await context.ProductVariantMaterials.IgnoreQueryFilters().GroupBy(x => x.MaterialId)
                    .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).Select(x => (x.Key, x.Count))
                .Concat((await context.PriceRules.Where(r => r.MaterialId != null).GroupBy(r => r.MaterialId!.Value)
                    .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).Select(x => (x.Key, x.Count)))
                .Concat((await context.QuoteRequests.Where(q => q.MaterialId != null).GroupBy(q => q.MaterialId!.Value)
                    .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).Select(x => (x.Key, x.Count))),
            AttributeKind.Size => (await context.ProductVariantSizes.IgnoreQueryFilters().GroupBy(x => x.SizeId)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).Select(x => (x.Key, x.Count)),
            AttributeKind.Style => (await context.Products.IgnoreQueryFilters().Where(p => p.StyleId != null).GroupBy(p => p.StyleId!.Value)
                    .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).Select(x => (x.Key, x.Count))
                .Concat((await context.ProductVariants.IgnoreQueryFilters().Where(v => v.StyleId != null).GroupBy(v => v.StyleId!.Value)
                    .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).Select(x => (x.Key, x.Count)))
                .Concat((await context.QuoteRequests.Where(q => q.StyleId != null).GroupBy(q => q.StyleId!.Value)
                    .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).Select(x => (x.Key, x.Count))),
            _ => []
        };

        return rows.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Sum(r => r.Count));
    }

    public Task<int> CountAllProductsInCategoryAsync(int categoryId, CancellationToken cancellationToken = default) =>
        context.Products.IgnoreQueryFilters().CountAsync(p => p.CategoryId == categoryId, cancellationToken);
}
