namespace FurnitureStore.Infrastructure.Persistence.Seed;

/// <summary>Startup database behaviour, bound from the "Database" section.</summary>
public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>Apply pending EF Core migrations at startup. Recommended only for local development.</summary>
    public bool ApplyMigrationsOnStartup { get; set; }

    /// <summary>Insert demo catalog data (categories, products, variants...) when the catalog is empty.</summary>
    public bool SeedDemoData { get; set; }
}

/// <summary>
/// Accounts created at startup, bound from the "Seed" section.
/// Passwords are intentionally empty in appsettings.json: provide them with
/// `dotnet user-secrets set "Seed:AdminPassword" "..."` or the environment variable Seed__AdminPassword.
/// </summary>
public sealed class SeedSettings
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; set; } = "admin@furniture.local";
    public string AdminFullName { get; set; } = "Quản trị viên";
    public string? AdminPassword { get; set; }

    public string DemoUserEmail { get; set; } = "khachhang@furniture.local";
    public string DemoUserFullName { get; set; } = "Khách hàng Demo";
    public string? DemoUserPassword { get; set; }
}
