using System.Net;
using System.Net.Http.Json;
using FurnitureStore.Tests.Infrastructure;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

public sealed class AuthorizationTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private sealed record Envelope<T>(bool Success, string Message, T? Data, List<string> Errors);
    private sealed record Me(string Id, string Email, string FullName, List<string> Roles);

    [Fact]
    public async Task Anonymous_AdminArea_RedirectsToLogin()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/account/login?ReturnUrl=%2Fadmin", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Customer_AdminArea_IsForbidden()
    {
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword);

        var response = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/account/access-denied", response.Headers.Location?.ToString());

        var denied = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Customer_CallingAdminArea_ViaAjax_Gets403Json_NotHtml()
    {
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword);
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/dashboard");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<Envelope<object>>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.Success);
    }

    [Fact]
    public async Task Admin_CanOpenDashboard()
    {
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

        var response = await client.GetAsync("/admin");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Tổng quan", html);
        Assert.Contains("noindex,nofollow", html);
    }

    [Fact]
    public async Task AdminMenuLink_IsShownOnlyToAdmins()
    {
        var admin = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        var customer = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword);

        Assert.Contains("Trang quản trị", await admin.GetStringAsync("/"));
        Assert.DoesNotContain("Trang quản trị", await customer.GetStringAsync("/"));
    }

    [Fact]
    public async Task Api_Me_Anonymous_Returns401JsonEnvelope()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/account/me");
        var body = await response.Content.ReadFromJsonAsync<Envelope<Me>>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.Contains("đăng nhập", body.Message);
    }

    [Fact]
    public async Task Api_Me_SignedIn_ReturnsUserAndRoles()
    {
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

        var body = await client.GetFromJsonAsync<Envelope<Me>>("/api/account/me");

        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.Equal(FurnitureStoreWebApplicationFactory.AdminEmail, body.Data!.Email);
        Assert.Contains("ADMIN", body.Data.Roles);
    }

    [Theory]
    [InlineData("/account/profile")]
    [InlineData("/account/changepassword")]
    public async Task AccountPages_RequireSignIn(string url)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(response.Headers.Contains("Referrer-Policy"));
    }
}
