namespace FurnitureStore.Application.Common.Interfaces;

/// <summary>
/// Commits changes tracked by the repositories of the current request as one unit.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Saves all pending changes. Throws <see cref="Exceptions.ConflictException"/> when the data was
    /// modified by someone else meanwhile, or when a unique value (SKU, slug, email...) already exists.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a database transaction (with the provider's retry strategy).
    /// The transaction is committed when the operation completes and rolled back if it throws.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}
