using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FurnitureStore.Tests.Infrastructure;

/// <summary>
/// Boots the real Web application in-memory for integration tests, backed by an in-memory SQLite
/// database (schema from the EF model, seeded with roles, test accounts and the demo catalog).
/// Clients talk HTTPS because auth / antiforgery cookies are Secure-only.
/// </summary>
public class FurnitureStoreWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@furniture.local";
    public const string AdminPassword = "Test@Admin123";
    public const string UserEmail = "khachhang@furniture.local";
    public const string UserPassword = "Test@User123";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FurnitureStoreWebApplicationFactory()
    {
        _connection.Open();
        EmailPickupDirectory = Path.Combine(Path.GetTempPath(), "furniturestore-tests", Guid.NewGuid().ToString("N"), "emails");
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    /// <summary>Folder where the pickup email sender writes emails during tests.</summary>
    public string EmailPickupDirectory { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Database:SeedDemoData", "true");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Seed:DemoUserEmail", UserEmail);
        builder.UseSetting("Seed:DemoUserPassword", UserPassword);
        builder.UseSetting("Email:Mode", "Pickup");
        builder.UseSetting("Email:PickupDirectory", EmailPickupDirectory);
        builder.UseSetting("RateLimiting:AuthenticationPermitsPerMinute", "1000");
        builder.UseSetting("RateLimiting:FormPermitsPerMinute", "1000");
        builder.UseSetting("RateLimiting:ApiPermitsPerMinute", "100000");
        builder.UseSetting("AI:RequestsPerMinute", "1000");
        builder.UseSetting("AI:ApiKey", ""); // never call a real AI provider from tests (even if AI__ApiKey is set on the machine)
        builder.UseSetting("DataProtection:KeysPath", Path.Combine(Directory.GetParent(EmailPickupDirectory)!.FullName, "keys"));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(_connection);
                // Queries loading several collections in one SQL statement must use split queries.
                options.ConfigureWarnings(w => w.Throw(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning));
                options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            try
            {
                var root = Directory.GetParent(EmailPickupDirectory)!.FullName;
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best effort clean-up of temporary email files.
            }
        }
    }
}
