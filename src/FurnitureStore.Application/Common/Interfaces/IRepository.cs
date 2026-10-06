using System.Linq.Expressions;
using FurnitureStore.Domain.Common;

namespace FurnitureStore.Application.Common.Interfaces;

/// <summary>
/// Basic persistence operations for an entity. Aggregate-specific queries (product search,
/// dashboard statistics...) live in dedicated repositories that extend this contract.
/// Changes are persisted by <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);

    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    void Update(T entity);

    void Remove(T entity);
}
