using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Infrastructure.Persistence;

public sealed class UnitOfWork(ApplicationDbContext context, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    // SQL Server error numbers for unique index / unique constraint violations.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning("Concurrency conflict while saving {Entities}",
                string.Join(", ", ex.Entries.Select(e => e.Metadata.ClrType.Name)));
            throw new ConflictException();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            logger.LogWarning("Unique constraint violation while saving {Entities}",
                string.Join(", ", ex.Entries.Select(e => e.Metadata.ClrType.Name)));
            throw new ConflictException("Dữ liệu đã tồn tại (trùng mã, slug hoặc email). Vui lòng kiểm tra lại.");
        }
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        // The retry strategy of SQL Server requires user transactions to run inside ExecuteAsync.
        var strategy = context.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var result = await operation(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) =>
        ExecuteInTransactionAsync<object?>(async ct =>
        {
            await operation(ct);
            return null;
        }, cancellationToken);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException switch
        {
            SqlException sql => sql.Number is UniqueIndexViolation or UniqueConstraintViolation,
            // SQLite (tests): "UNIQUE constraint failed"
            { } inner => inner.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}
