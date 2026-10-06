using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

/// <summary>Phase 10: SEO files and metadata, security headers / CSP, compression, caching, rate limits and log hygiene.</summary>
public sealed partial class SeoAndSecurityTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    // ------------------------------------------------------------------ SEO

    [Fact]
    public async Task RobotsTxt_BlocksPrivateAreas_AndPointsToTheSitemap()
    {
        var response = await factory.CreateClient().GetAsync("/robots.txt");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        foreach (var path in new[] { "/admin", "/account", "/cart", "/checkout", "/api" })
        {
            Assert.Contains($"Disallow: {path}\n", text);
        }

        Assert.Contains("Sitemap: https://localhost:7160/sitemap.xml", text);
    }

    [Fact]
    public async Task RobotsTxt_And_Pages_AreNoindex_WhenIndexingIsDisabled()
    {
        using var staging = factory.WithWebHostBuilder(b => b.UseSetting("Seo:AllowIndexing", "false"));
        var client = staging.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        Assert.Equal("User-agent: *\nDisallow: /\n", await client.GetStringAsync("/robots.txt"));
        Assert.Contains("<meta name=\"robots\" content=\"noindex,nofollow\" />", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task Sitemap_ListsEveryActiveProduct_WithValidXml()
    {
        var response = await factory.CreateClient().GetAsync("/sitemap.xml");
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = xml.Root!.Elements(ns + "url").Select(u => u.Element(ns + "loc")!.Value).ToList();

        Assert.Equal("application/xml", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("https://localhost:7160/", urls);
        Assert.Contains("https://localhost:7160/products?category=sofa", urls);
        var activeSlugs = await DbAsync(db => db.Products.Where(p => p.Status == ProductStatus.Active).Select(p => p.Slug).ToListAsync());
        Assert.Equal(activeSlugs.Count, urls.Count(u => u.StartsWith("https://localhost:7160/products/")));
        Assert.All(activeSlugs, slug => Assert.Contains($"https://localhost:7160/products/{slug}", urls));
        Assert.All(xml.Root.Elements(ns + "url").Select(u => u.Element(ns + "lastmod")?.Value).Where(v => v is not null),
            v => Assert.Matches("^\\d{4}-\\d{2}-\\d{2}$", v));
    }

    [Fact]
    public async Task ProductPage_HasCanonicalOpenGraphAndStructuredData()
    {
        var product = await DbAsync(db => db.Products.Include(p => p.Category).FirstAsync(p => p.Status == ProductStatus.Active && p.Category.ParentId != null));
        var html = await factory.CreateClient().GetStringAsync($"/products/{product.Slug}");

        Assert.Contains($"<link rel=\"canonical\" href=\"https://localhost:7160/products/{product.Slug}\" />", html);
        Assert.Contains("<meta property=\"og:type\" content=\"product\" />", html);
        Assert.Contains("<meta property=\"og:image\" content=\"https://localhost:7160/images/og-default.png\" />", html); // demo images are SVG
        Assert.Contains("<meta name=\"robots\" content=\"index,follow\" />", html);

        var blocks = JsonLdRegex().Matches(html).Select(m => JsonDocument.Parse(WebUtility.HtmlDecode(m.Groups[1].Value)).RootElement).ToList();
        var productLd = blocks.Single(b => b.GetProperty("@type").GetString() == "Product");
        Assert.Equal(product.Name, productLd.GetProperty("name").GetString());
        Assert.Equal("VND", productLd.GetProperty("offers").GetProperty("priceCurrency").GetString());
        var crumbs = blocks.Single(b => b.GetProperty("@type").GetString() == "BreadcrumbList").GetProperty("itemListElement");
        Assert.Equal(4, crumbs.GetArrayLength());
        Assert.Equal(product.Name, crumbs[3].GetProperty("name").GetString());
    }

    [Fact]
    public async Task HomePage_DescribesTheStore_ForSearchEngines()
    {
        var html = await factory.CreateClient().GetStringAsync("/");
        var types = JsonLdRegex().Matches(html).Select(m => JsonDocument.Parse(WebUtility.HtmlDecode(m.Groups[1].Value)).RootElement.GetProperty("@type").GetString()).ToList();

        Assert.Contains("FurnitureStore", types);
        Assert.Contains("WebSite", types);
        Assert.Contains("<meta property=\"og:image:width\" content=\"1200\" />", html);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/images/og-default.png")).StatusCode);
    }

    [Theory]
    [InlineData("/cart", false)]
    [InlineData("/account/login", false)]
    [InlineData("/checkout", true)]
    [InlineData("/account/orders", true)]
    public async Task PrivatePages_AreNoindex(string path, bool signedIn)
    {
        var client = signedIn
            ? await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword)
            : factory.CreateClient();
        var response = await client.GetAsync(path);
        var html = response.StatusCode == HttpStatusCode.Redirect ? await client.GetStringAsync(response.Headers.Location) : await response.Content.ReadAsStringAsync();

        Assert.Contains("<meta name=\"robots\" content=\"noindex,nofollow\" />", html);
    }

    // ------------------------------------------------------------------ security headers & CSP

    [Fact]
    public async Task Pages_SendAStrictContentSecurityPolicy_AndSecurityHeaders()
    {
        var response = await factory.CreateClient().GetAsync("/");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains("script-src 'self';", csp);                    // no inline / third-party scripts
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("frame-ancestors 'self'", csp);
        Assert.Contains("connect-src 'self' wss://localhost", csp);   // SignalR only to this host
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.False(response.Headers.Contains("Server"));

        var svg = await factory.CreateClient().GetAsync("/images/placeholder/sofa.svg?color=b98a5a");
        Assert.StartsWith("default-src 'none'", svg.Headers.GetValues("Content-Security-Policy").Single()); // stricter policy kept
    }

    [Fact]
    public async Task NoPage_ContainsInlineExecutableScripts()
    {
        // The CSP forbids inline scripts: every page must only reference script files (JSON data blocks are allowed).
        var anonymous = factory.CreateClient();
        var customer = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword);
        var admin = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        var slug = await DbAsync(db => db.Products.Where(p => p.Status == ProductStatus.Active).Select(p => p.Slug).FirstAsync());
        var productId = await DbAsync(db => db.Products.Select(p => p.Id).FirstAsync());

        var pages = new (HttpClient Client, string Url)[]
        {
            (anonymous, "/"), (anonymous, "/products"), (anonymous, $"/products/{slug}"), (anonymous, "/cart"), (anonymous, "/contact"),
            (anonymous, "/tu-van"), (anonymous, "/bao-gia"), (anonymous, "/account/login"), (anonymous, "/account/register"),
            (customer, "/account/profile"), (customer, "/account/changepassword"), (customer, "/account/orders"), (customer, "/checkout"),
            (admin, "/admin"), (admin, "/admin/products/create"), (admin, $"/admin/products/edit/{productId}"),
            (admin, "/admin/attributes/colors/create"), (admin, "/admin/chat"), (admin, "/admin/quotes"), (admin, "/admin/price-rules"),
            (admin, "/admin/coupons"), (admin, "/admin/coupons/create"), (admin, "/admin/coupons/1"), (admin, "/admin/coupons/1/edit"),
            (admin, "/admin/products/qrlabels"), (admin, $"/admin/products/qrlabels?id={productId}")
        };

        foreach (var (client, url) in pages)
        {
            var response = await client.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.Redirect) response = await client.GetAsync(response.Headers.Location);
            var html = await response.Content.ReadAsStringAsync();
            var inline = InlineScriptRegex().Matches(html).Select(m => m.Value).ToList();
            Assert.True(inline.Count == 0, $"{url} has inline script(s): {string.Join(" | ", inline)}");
            Assert.DoesNotMatch(" on(click|change|submit|load|input|error)=\"", html);
        }
    }

    // ------------------------------------------------------------------ performance

    [Fact]
    public async Task Responses_AreCompressed_AndStaticFilesAreCached()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.AcceptEncoding.ParseAdd("br");
        var page = await client.SendAsync(request);
        Assert.Equal("br", page.Content.Headers.ContentEncoding.Single());

        var versioned = await client.GetAsync("/css/site.css?v=abc");
        Assert.Equal("public,max-age=31536000,immutable", versioned.Headers.CacheControl!.ToString().Replace(" ", ""));
        var library = await client.GetAsync("/lib/bootstrap/css/bootstrap.min.css");
        Assert.Equal(TimeSpan.FromDays(7), library.Headers.CacheControl!.MaxAge);
    }

    [Fact]
    public async Task Api_HasAGlobalRateLimit()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:ApiPermitsPerMinute", "3"));
        var client = limited.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++) statuses.Add((await client.GetAsync("/api/categories")).StatusCode);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode); // pages are not limited by it
    }

    /// <summary>Simulates requests arriving from a proxy at 127.0.0.1 (TestServer has no remote address).</summary>
    private sealed class FromLoopbackStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use(nextMiddleware => context =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClientIp_ComesFromXForwardedFor_OnlyBehindATrustedProxy(bool trusted)
    {
        using var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:ApiPermitsPerMinute", "2");
            if (trusted) b.UseSetting("ReverseProxy:KnownProxies:0", "127.0.0.1");
            b.ConfigureServices(services => services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, FromLoopbackStartupFilter>());
        });
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        async Task<HttpStatusCode> CallAs(string ip)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/categories");
            request.Headers.Add("X-Forwarded-For", ip);
            return (await client.SendAsync(request)).StatusCode;
        }

        var first = new[] { await CallAs("203.0.113.1"), await CallAs("203.0.113.1") };
        var otherClient = await CallAs("203.0.113.2");

        Assert.All(first, s => Assert.Equal(HttpStatusCode.OK, s));
        // Trusted proxy: another client IP has its own quota. Untrusted header: everyone shares the proxy's quota.
        Assert.Equal(trusted ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, otherClient);
    }

    // ------------------------------------------------------------------ logging

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Lines);

        public void Dispose() { }

        private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }

    [Fact]
    public async Task Logs_NeverContainPasswordsApiKeysOrTokens()
    {
        const string apiKey = "sk-test-SECRET-9876543210";
        const string password = "Pw@SecretValue123";
        var capture = new CapturingLoggerProvider();
        using var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("AI:ApiKey", apiKey);
            b.UseSetting("AI:BaseUrl", "http://127.0.0.1:9/v1/"); // nothing listens there: the call fails fast
            b.ConfigureLogging(logging => logging.AddProvider(capture).SetMinimumLevel(LogLevel.Trace));
        });
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var email = await RegisterAsync(client, password: password);
        await client.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = await GetAntiforgeryTokenAsync(client, "/account/profile") }));
        await LoginAsync(client, email, "Wrong@Password999");
        await LoginAsync(client, email, password);
        await client.GetAsync("/account/resetpassword?email=x%40y.z&code=RESET-TOKEN-SECRET-42");

        var html = await client.GetStringAsync("/");
        var csrf = WebUtility.HtmlDecode(CsrfMetaRegex().Match(html).Groups[1].Value);
        var ai = new HttpRequestMessage(HttpMethod.Post, "/api/ai/chat") { Content = JsonContent.Create(new { message = "sofa xám" }) };
        ai.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ai)).StatusCode); // AI unreachable → rule-based fallback

        var log = string.Join("\n", capture.Lines);
        Assert.Contains("AI provider unreachable", log);          // the failure is logged...
        Assert.Contains("/account/login", log);                    // ...and so is the access log
        Assert.DoesNotContain(apiKey, log);                        // ...but never the key,
        Assert.DoesNotContain(password, log);                      // the password,
        Assert.DoesNotContain("Wrong@Password999", log);
        Assert.DoesNotContain("RESET-TOKEN-SECRET-42", log);       // or tokens from query strings
    }

    [GeneratedRegex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLdRegex();

    [GeneratedRegex("<script(?![^>]*\\bsrc=)(?![^>]*type=\"application/(ld\\+)?json\")[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScriptRegex();

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMetaRegex();
}
