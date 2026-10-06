using FurnitureStore.Domain.Constants;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Infrastructure.Persistence.Seed;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Web;

public sealed class DatabaseStartupTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private AsyncServiceScope CreateScope()
    {
        // Creating a client forces the host (and the startup database initializer) to run.
        factory.CreateClient();
        return factory.Services.CreateAsyncScope();
    }

    [Fact]
    public async Task Startup_SeedsAllRoles()
    {
        await using var scope = CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var role in AppRoles.All)
        {
            Assert.True(await roleManager.RoleExistsAsync(role), $"Role {role} missing");
        }
    }

    [Fact]
    public async Task Startup_SeedsAdminWithAdminRole_AndPasswordFromConfiguration()
    {
        await using var scope = CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await userManager.FindByEmailAsync(FurnitureStoreWebApplicationFactory.AdminEmail);

        Assert.NotNull(admin);
        Assert.True(await userManager.IsInRoleAsync(admin, AppRoles.Admin));
        Assert.True(await userManager.CheckPasswordAsync(admin, FurnitureStoreWebApplicationFactory.AdminPassword));
        // Stored as a hash, never in clear text.
        Assert.DoesNotContain(FurnitureStoreWebApplicationFactory.AdminPassword, admin.PasswordHash);
    }

    [Fact]
    public async Task Startup_SeedsDemoUserWithUserRoleOnly()
    {
        await using var scope = CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(FurnitureStoreWebApplicationFactory.UserEmail);

        Assert.NotNull(user);
        Assert.True(await userManager.IsInRoleAsync(user, AppRoles.User));
        Assert.False(await userManager.IsInRoleAsync(user, AppRoles.Admin));
    }

    [Fact]
    public async Task Startup_SeedsCatalogAndStoreInformation()
    {
        await using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.True(await context.Products.CountAsync() >= 30);
        Assert.True(await context.Categories.CountAsync() >= 10);
        Assert.Equal(1, await context.StoreInformation.CountAsync());
    }

    [Fact]
    public async Task Startup_UpdatesTheFormerDefaultTagline_ButKeepsOneTheAdminWrote()
    {
        await using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var store = await context.StoreInformation.SingleAsync();

        store.Tagline = "Không gian đẹp - Nội thất chất lượng";       // seeded by an earlier version
        await context.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        await context.Entry(store).ReloadAsync();
        Assert.Equal("Bàn ghế ăn gỗ sồi Nga - Đóng tại xưởng", store.Tagline);

        store.Tagline = "Khẩu hiệu do admin viết";
        await context.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        await context.Entry(store).ReloadAsync();
        Assert.Equal("Khẩu hiệu do admin viết", store.Tagline);
    }
}
