using FurnitureStore.Domain.Constants;
using FurnitureStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Infrastructure.Persistence.Seed;

/// <summary>Creates the roles and, when a password is configured, the default admin / demo accounts.</summary>
public sealed class IdentitySeeder(
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<SeedSettings> seedOptions,
    TimeProvider timeProvider,
    ILogger<IdentitySeeder> logger)
{
    private static readonly Dictionary<string, string> RoleDescriptions = new()
    {
        [AppRoles.Admin] = "Quản trị toàn bộ hệ thống",
        [AppRoles.User] = "Khách hàng đã đăng ký",
        [AppRoles.Staff] = "Nhân viên bán hàng / tư vấn",
        [AppRoles.Customer] = "Khách hàng (dự phòng mở rộng)"
    };

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var role in AppRoles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new ApplicationRole(role, RoleDescriptions.GetValueOrDefault(role)));
                LogResult(result, "role", role);
            }
        }

        var settings = seedOptions.Value;
        await EnsureUserAsync(settings.AdminEmail, settings.AdminFullName, settings.AdminPassword, AppRoles.Admin, "Seed:AdminPassword");
        await EnsureUserAsync(settings.DemoUserEmail, settings.DemoUserFullName, settings.DemoUserPassword, AppRoles.User, "Seed:DemoUserPassword");
    }

    private async Task EnsureUserAsync(string email, string fullName, string? password, string role, string passwordSettingKey)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning(
                    "Account {Email} was not created because {SettingKey} is not configured. " +
                    "Set it with 'dotnet user-secrets set \"{SettingKey}\" \"<password>\"' or an environment variable.",
                    email, passwordSettingKey, passwordSettingKey);
                return;
            }

            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                IsActive = true,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime
            };

            var created = await userManager.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                LogResult(created, "user", email);
                return;
            }

            logger.LogInformation("Seeded account {Email} with role {Role}", email, role);
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            var added = await userManager.AddToRoleAsync(user, role);
            LogResult(added, "role assignment", $"{email} → {role}");
        }
    }

    private void LogResult(IdentityResult result, string kind, string name)
    {
        if (result.Succeeded)
        {
            logger.LogInformation("Seeded {Kind} {Name}", kind, name);
            return;
        }

        // Only error descriptions are logged, never the password.
        logger.LogError("Failed to seed {Kind} {Name}: {Errors}", kind, name,
            string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
