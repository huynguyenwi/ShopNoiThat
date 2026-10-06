namespace FurnitureStore.Application.Catalog.Admin;

/// <summary>Queries that only the back-office needs.</summary>
public interface ICatalogAdminRepository
{
    /// <summary>How many records reference each attribute (variants, products, price rules, quotes).</summary>
    Task<IReadOnlyDictionary<int, int>> GetAttributeUsageAsync(AttributeKind kind, CancellationToken cancellationToken = default);

    /// <summary>Products in a category, including soft-deleted ones (they still hold the foreign key).</summary>
    Task<int> CountAllProductsInCategoryAsync(int categoryId, CancellationToken cancellationToken = default);
}
