using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Infrastructure.Persistence.Seed;

/// <summary>
/// Runs at application startup: applies migrations (when enabled), then seeds roles, the admin account,
/// store information and - when enabled - the demo catalog.
/// </summary>
public sealed class DatabaseInitializer(
    ApplicationDbContext context,
    IdentitySeeder identitySeeder,
    CatalogSeeder catalogSeeder,
    SalesSeeder salesSeeder,
    DemoActivitySeeder demoActivitySeeder,
    AiKnowledgeSeeder aiKnowledgeSeeder,
    PriceRuleSeeder priceRuleSeeder,
    IOptions<DatabaseSettings> databaseOptions,
    IOptions<ApplicationSettings> applicationOptions,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = databaseOptions.Value;

        if (context.Database.IsSqlServer())
        {
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
            {
                if (!settings.ApplyMigrationsOnStartup)
                {
                    logger.LogWarning(
                        "Database has {Count} pending migration(s): {Migrations}. Run 'dotnet ef database update' " +
                        "or set Database:ApplyMigrationsOnStartup=true. Seeding skipped.",
                        pending.Count, string.Join(", ", pending));
                    return;
                }

                logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await context.Database.MigrateAsync(cancellationToken);
            }
        }
        else
        {
            // Non-SQL Server providers (SQLite in tests) build the schema straight from the model.
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }

        await identitySeeder.SeedAsync(cancellationToken);
        await SeedStoreInfoAsync(cancellationToken);
        await aiKnowledgeSeeder.SeedAsync(cancellationToken);

        if (settings.SeedDemoData)
        {
            await catalogSeeder.SeedAsync(cancellationToken);
            await salesSeeder.SeedAsync(cancellationToken);
            await demoActivitySeeder.SeedAsync(cancellationToken);
        }

        // After the catalog, so material-specific prices can reference the materials.
        await priceRuleSeeder.SeedAsync(cancellationToken);
        await catalogSeeder.BackfillSearchTextAsync(cancellationToken);
    }

    /// <summary>Tagline seeded by earlier versions; a store still using it never edited it, so it follows the configured one.</summary>
    private const string FormerDefaultTagline = "Không gian đẹp - Nội thất chất lượng";

    private async Task SeedStoreInfoAsync(CancellationToken cancellationToken)
    {
        if (await context.StoreInformation.FirstOrDefaultAsync(cancellationToken) is { } existing)
        {
            var tagline = applicationOptions.Value.Tagline;
            if (existing.Tagline == FormerDefaultTagline && tagline != FormerDefaultTagline)
            {
                existing.Tagline = tagline;
                await context.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Updated the unedited store tagline to the configured one");
            }

            return;
        }

        var site = applicationOptions.Value;
        var store = site.Store;
        context.StoreInformation.Add(new StoreInfo
        {
            Name = store.Name,
            Tagline = site.Tagline,
            About = "Xưởng và showroom nội thất gỗ, sản xuất trực tiếp và nhận đóng theo yêu cầu.",
            Address = store.Address,
            WorkshopAddress = store.WorkshopAddress,
            Hotline = store.Hotline,
            Email = store.Email,
            OpeningHours = store.OpeningHours,
            FacebookUrl = store.FacebookUrl,
            TikTokUrl = store.TikTokUrl,
            ZaloUrl = store.ZaloUrl,
            GoogleMapsEmbedUrl = store.GoogleMapsEmbedUrl
        });

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded store information from configuration");
    }
}

public static class DatabaseInitializerExtensions
{
    /// <summary>Creates a scope and runs <see cref="DatabaseInitializer"/>.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync(cancellationToken);
    }
}
