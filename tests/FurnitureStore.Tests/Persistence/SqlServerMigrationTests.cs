using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Infrastructure.Persistence.Seed;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FurnitureStore.Tests.Persistence;

/// <summary>
/// Runs only where SQL Server LocalDB exists (Windows dev machines). Set SKIP_SQLSERVER_TESTS=1 to skip.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("SKIP_SQLSERVER_TESTS") == "1")
        {
            Skip = "SQL Server LocalDB is not available in this environment.";
        }
    }
}

[Trait("Category", "SqlServer")]
public sealed class SqlServerMigrationTests
{
    private static ApplicationDbContext CreateSqlServerContext(string database) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;TrustServerCertificate=True")
            .AddInterceptors(new FurnitureStore.Infrastructure.Persistence.Interceptors.AuditableEntityInterceptor(new TestCurrentUser(), TimeProvider.System))
            .Options);

    [Fact]
    public void Model_HasNoChangesMissingFromMigrations()
    {
        using var context = CreateSqlServerContext("FurnitureStore_ModelCheck");

        Assert.False(context.Database.HasPendingModelChanges(),
            "The EF model changed but no migration was added. Run 'dotnet ef migrations add <Name>'.");
    }

    [SqlServerFact]
    public async Task Migrations_ApplyOnFreshSqlServerDatabase_AndDemoCatalogSeeds()
    {
        var database = $"FurnitureStore_Test_{Guid.NewGuid():N}";
        await using var context = CreateSqlServerContext(database);
        try
        {
            await context.Database.MigrateAsync();
            await new CatalogSeeder(context, TimeProvider.System, NullLogger<CatalogSeeder>.Instance).SeedAsync();

            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.Equal(CatalogSeedData.Products.Length, await context.Products.CountAsync());
            Assert.True(await context.ProductVariants.CountAsync() >= 60);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }
}
