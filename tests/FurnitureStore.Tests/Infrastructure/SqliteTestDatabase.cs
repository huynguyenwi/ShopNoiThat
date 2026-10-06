using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Infrastructure.Persistence.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStore.Tests.Infrastructure;

/// <summary>
/// Isolated in-memory SQLite database built from the EF Core model. The database lives as long as this
/// object (the connection stays open), so several DbContext instances can share it - e.g. to simulate
/// two users editing the same row.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase(TimeProvider? timeProvider = null)
    {
        TimeProvider = timeProvider ?? FixedTimeProvider.At(2026, 9, 1);
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public TimeProvider TimeProvider { get; }

    public ApplicationDbContext CreateContext(ICurrentUserService? currentUser = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditableEntityInterceptor(currentUser ?? new TestCurrentUser(), TimeProvider))
            .Options;

        return new ApplicationDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
