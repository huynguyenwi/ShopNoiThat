using System.Globalization;
using System.Text;
using FurnitureStore.Application;
using FurnitureStore.Infrastructure;
using FurnitureStore.Infrastructure.Persistence.Seed;
using FurnitureStore.Web;
using FurnitureStore.Web.Hubs;
using FurnitureStore.Web.Infrastructure;

// Parse numbers / dates the same way on every server locale (e.g. "16900000.5" from <input type="number">).
// Output for users is formatted explicitly in Vietnamese by FurnitureStore.Web.Infrastructure.Format.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

// Vietnamese text in console logs: Windows consoles default to an OEM code page.
if (OperatingSystem.IsWindows())
{
    try
    {
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        // No console attached (IIS, Windows service): logs go to other providers.
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddWebServices(builder.Configuration)
    .AddKeyStorage(builder.Configuration, builder.Environment);
var behindReverseProxy = builder.Services.AddReverseProxySupport(builder.Configuration);

var app = builder.Build();

if (behindReverseProxy)
{
    app.UseForwardedHeaders(); // must run first: client IP / scheme for everything below
}

// Applies migrations (if Database:ApplyMigrationsOnStartup) and seeds roles, admin account and demo data.
await app.Services.InitializeDatabaseAsync();

// Handled by GlobalExceptionHandler: JSON envelope for /api/*, friendly error page for MVC.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    ExceptionHandlingPath = "/Error",
    AllowStatusCode404Response = true
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Empty 4xx/5xx responses: JSON for API calls, error page for browser requests.
app.UseWhen(ctx => ctx.IsApiRequest(), api => api.UseStatusCodePages(ApiStatusCodePages.WriteResponseAsync));
app.UseWhen(ctx => !ctx.IsApiRequest(), mvc => mvc.UseStatusCodePagesWithReExecute("/Error/{0}"));

app.UseSecurityHeaders();
app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = StaticFileCaching.Apply });
app.UseHttpLogging(); // after static files: only pages and API calls are logged

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<ChatHub>(ChatHub.Path);
app.MapHealthChecks("/health");

await app.RunAsync();

// Exposes the implicit Program class to WebApplicationFactory in integration tests.
public partial class Program;
