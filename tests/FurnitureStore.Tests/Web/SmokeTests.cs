using System.Net;
using System.Net.Http.Json;
using FurnitureStore.Tests.Infrastructure;

namespace FurnitureStore.Tests.Web;

public sealed class SmokeTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private sealed record ApiEnvelope(bool Success, string Message, object? Data, List<string> Errors);

    [Fact]
    public async Task HomePage_ReturnsOk_WithHeroAndSeoMetadata()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Bộ bàn ăn gỗ sồi Nga", html);
        Assert.Contains("Xem bộ bàn ăn", html);
        Assert.Contains("<meta name=\"description\"", html);
        Assert.Contains("property=\"og:title\"", html);
        Assert.Contains("<meta name=\"csrf-token\"", html);
    }

    [Fact]
    public async Task UnknownPage_ReturnsFriendly404Page()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/this-page-does-not-exist");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Không tìm thấy trang", html);
    }

    [Fact]
    public async Task UnknownApiEndpoint_ReturnsStandardJsonEnvelope()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/this-endpoint-does-not-exist");
        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.False(string.IsNullOrWhiteSpace(body.Message));
        Assert.Null(body.Data);
        Assert.NotNull(body.Errors);
    }

    [Fact]
    public async Task SearchQuery_ReflectedInHeader_IsHtmlEncoded()
    {
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/?q=%3Cscript%3Ealert(1)%3C%2Fscript%3E");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    [Fact]
    public async Task ApiJson_KeepsVietnameseCharactersReadable()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Không tìm thấy tài nguyên", body);
    }

    [Fact]
    public async Task ErrorPage_RequestedDirectly_ReturnsNotFound()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/Error/500");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/lib/bootstrap/css/bootstrap.min.css")]
    [InlineData("/lib/bootstrap/js/bootstrap.bundle.min.js")]
    [InlineData("/lib/bootstrap-icons/bootstrap-icons.min.css")]
    [InlineData("/css/site.css")]
    [InlineData("/js/site.js")]
    [InlineData("/images/logo-mark.svg")]
    [InlineData("/images/hero-living-room.svg")]
    public async Task StaticAssets_AreServed(string path)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
