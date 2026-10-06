using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Qr;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

/// <summary>Product and order QR codes: images, what scanning opens (and for whom), where the codes are shown.</summary>
public sealed partial class QrWebTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private string BaseUrl => factory.Services.GetRequiredService<IOptions<ApplicationSettings>>().Value.BaseUrl.TrimEnd('/');
    private IQrCodeRenderer Renderer => factory.Services.GetRequiredService<IQrCodeRenderer>();

    private Task<HttpClient> AdminAsync() =>
        CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<(int Id, string Slug)> ProductAsync(string sku) =>
        DbAsync(db => db.Products.Where(p => p.Sku == sku).Select(p => new ValueTuple<int, string>(p.Id, p.Slug)).SingleAsync());

    /// <summary>Registers a customer and places an order through the checkout form. Returns the client and the order code.</summary>
    private async Task<(HttpClient Client, string Code, string Email)> CustomerWithOrderAsync()
    {
        var client = factory.CreateClient();
        var email = await RegisterAsync(client);
        var variantId = await DbAsync(db => db.ProductVariants.Where(v => v.Product.Sku == "GA-CURVE" && v.StockQuantity >= 3).Select(v => v.Id).FirstAsync());
        var html = await client.GetStringAsync("/");
        var add = new HttpRequestMessage(HttpMethod.Post, "/api/cart") { Content = JsonContent.Create(new { variantId, quantity = 1 }) };
        add.Headers.Add("X-CSRF-TOKEN", WebUtility.HtmlDecode(CsrfMetaRegex().Match(html).Groups[1].Value));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(add)).StatusCode);

        var placed = await PostFormAsync(client, "/checkout", "/checkout", new Dictionary<string, string>
        {
            ["Command.FullName"] = "Khách Quét Mã",
            ["Command.Phone"] = "0912345678",
            ["Command.Email"] = "qr@example.com",
            ["Command.Province"] = "TP. Hồ Chí Minh",
            ["Command.Ward"] = "Phường Bến Nghé",
            ["Command.AddressLine"] = "12 Lê Lợi",
            ["Command.PaymentMethod"] = "COD",
            ["Command.SaveAddress"] = "false"
        });
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        var code = placed.Headers.Location!.ToString().Split('/').Last();
        return (client, code, email);
    }

    // ------------------------------------------------------------------ Images

    [Fact]
    public async Task ProductQrImages_EncodeTheStableProductAddress()
    {
        var (id, _) = await ProductAsync("SF-OSLO");
        var client = factory.CreateClient();

        var svg = await client.GetAsync($"/qr/products/{id}.svg");
        Assert.Equal(HttpStatusCode.OK, svg.StatusCode);
        Assert.Equal("image/svg+xml", svg.Content.Headers.ContentType!.MediaType);
        Assert.Contains("public", svg.Headers.CacheControl!.ToString());
        Assert.Equal(Renderer.ToSvg($"{BaseUrl}/q/p/{id}"), await svg.Content.ReadAsStringAsync());

        var png = await client.GetAsync($"/qr/products/{id}.png");
        Assert.Equal("image/png", png.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Renderer.ToPng($"{BaseUrl}/q/p/{id}", QrLinks.DefaultPngScale), await png.Content.ReadAsByteArrayAsync());

        var download = await client.GetAsync($"/qr/products/{id}.png?download=true");
        Assert.Equal($"qr-san-pham-{id}.png", download.Content.Headers.ContentDisposition!.FileNameStar ?? download.Content.Headers.ContentDisposition.FileName);

        // Oversized requests are clamped, not honoured.
        var huge = await client.GetByteArrayAsync($"/qr/products/{id}.png?scale=500");
        Assert.Equal(Renderer.ToPng($"{BaseUrl}/q/p/{id}", QrLinks.MaxPngScale), huge);
    }

    [Theory]
    [InlineData("DH260930-ABCDE")]   // any well-formed code: nothing is looked up, so nothing is revealed
    [InlineData("DH000000-ZZZZZ")]
    public async Task OrderQrImages_AreDrawnForAnyWellFormedCode(string code)
    {
        var response = await factory.CreateClient().GetAsync($"/qr/orders/{code}.svg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Renderer.ToSvg($"{BaseUrl}/q/o/{code}"), await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/qr/orders/abc.svg")]            // lower-case / too short
    [InlineData("/qr/orders/DH%3Cscript%3E.svg")]  // not an order code
    [InlineData("/q/o/%3Cscript%3E")]
    [InlineData("/q/p/999999")]
    public async Task MalformedOrUnknownTargets_AreNotFound(string url)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync(url)).StatusCode);
    }

    // ------------------------------------------------------------------ Scanning a product code

    [Fact]
    public async Task ScanningAProductCode_OpensItsCurrentPage_EvenAfterTheSlugChanged()
    {
        var (id, slug) = await ProductAsync("BA-WALNUT");
        var client = factory.CreateClient();

        var first = await client.GetAsync($"/q/p/{id}");
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal($"/products/{slug}", first.Headers.Location!.ToString());

        const string renamed = "ban-an-go-oc-cho-doi-ten";
        await DbAsync(db => db.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Slug, renamed)));
        try
        {
            var after = await client.GetAsync($"/q/p/{id}");
            Assert.Equal($"/products/{renamed}", after.Headers.Location!.ToString()); // printed labels keep working
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/products/{renamed}")).StatusCode);
        }
        finally
        {
            await DbAsync(db => db.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Slug, slug)));
        }
    }

    [Fact]
    public async Task ScanningAHiddenProduct_OnlyOpensForAdmins_InTheBackOffice()
    {
        var (id, _) = await ProductAsync("KTV-SLIM");
        await DbAsync(db => db.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ProductStatus.Draft)));
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/q/p/{id}")).StatusCode);

            var admin = await (await AdminAsync()).GetAsync($"/q/p/{id}");
            Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode);
            Assert.Equal($"/admin/products/edit/{id}", admin.Headers.Location!.ToString());
        }
        finally
        {
            await DbAsync(db => db.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ProductStatus.Active)));
        }
    }

    // ------------------------------------------------------------------ Scanning an order code

    [Fact]
    public async Task ScanningAnOrderCode_OpensItOnlyForItsCustomerAndAdmins()
    {
        var (customer, code, _) = await CustomerWithOrderAsync();
        var orderId = await DbAsync(db => db.Orders.Where(o => o.OrderCode == code).Select(o => o.Id).SingleAsync());

        // Anonymous: sign in first - for real and made-up codes alike (no hint whether the order exists).
        var anonymous = factory.CreateClient();
        foreach (var scanned in new[] { code, "DH000000-ZZZZZ" })
        {
            var response = await anonymous.GetAsync($"/q/o/{scanned}");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("https://localhost/account/login?ReturnUrl=%2Fq%2Fo%2F", response.Headers.Location!.ToString());
        }

        var own = await customer.GetAsync($"/q/o/{code.ToLowerInvariant()}");
        Assert.Equal($"/account/orders/{code}", own.Headers.Location!.ToString());

        var stranger = factory.CreateClient();
        await RegisterAsync(stranger);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/q/o/{code}")).StatusCode);

        var admin = await (await AdminAsync()).GetAsync($"/q/o/{code}");
        Assert.Equal($"/admin/orders/details/{orderId}", admin.Headers.Location!.ToString());
    }

    [Fact]
    public async Task SigningInFromAScannedOrderCode_LandsOnTheOrder()
    {
        var (_, code, email) = await CustomerWithOrderAsync();

        var phone = factory.CreateClient();
        var login = await LoginAsync(phone, email, "Khach@Hang123", returnUrl: $"/q/o/{code}");
        Assert.Equal($"/q/o/{code}", login.Headers.Location!.ToString());
        var resolved = await phone.GetAsync(login.Headers.Location!.ToString());
        Assert.Equal($"/account/orders/{code}", resolved.Headers.Location!.ToString());
    }

    // ------------------------------------------------------------------ Where the codes are shown

    [Fact]
    public async Task ProductPage_OffersTheQrCode()
    {
        var (id, slug) = await ProductAsync("SF-OSLO");

        var html = await factory.CreateClient().GetStringAsync($"/products/{slug}");

        Assert.Contains("data-bs-target=\"#productQrModal\"", html);
        Assert.Contains($"src=\"/qr/products/{id}.svg\"", html);
        Assert.Contains($"/qr/products/{id}.png?download=true", html);
    }

    [Fact]
    public async Task AdminProductPage_ShowsTheCode_WithItsAddress_AndPrintsLabels()
    {
        var (id, _) = await ProductAsync("SF-OSLO");
        var admin = await AdminAsync();

        var edit = await admin.GetStringAsync($"/admin/products/edit/{id}");
        Assert.Contains($"{BaseUrl}/q/p/{id}", edit);
        Assert.Contains($"/admin/products/qrlabels?id={id}", edit);

        var single = await admin.GetStringAsync($"/admin/products/qrlabels?id={id}");
        Assert.Single(QrLabelRegex().Matches(single));
        Assert.Contains($"src=\"/qr/products/{id}.svg\"", single);
        Assert.DoesNotContain("admin-sidebar", single); // print layout

        var categoryId = await DbAsync(db => db.Products.Where(p => p.Id == id).Select(p => p.CategoryId).SingleAsync());
        var inCategory = await DbAsync(db => db.Products.CountAsync(p => p.CategoryId == categoryId));
        var sheet = await admin.GetStringAsync($"/admin/products/qrlabels?categoryId={categoryId}&page=3&pageSize=5");
        Assert.Equal(inCategory, QrLabelRegex().Matches(sheet).Count); // the whole filtered list, not the current page
    }

    [Fact]
    public async Task OrderPages_AndConfirmationEmail_ShowTheOrderCode()
    {
        var (customer, code, _) = await CustomerWithOrderAsync();
        var orderId = await DbAsync(db => db.Orders.Where(o => o.OrderCode == code).Select(o => o.Id).SingleAsync());

        Assert.Contains($"src=\"/qr/orders/{code}.svg\"", await customer.GetStringAsync($"/checkout/success/{code}"));
        Assert.Contains($"src=\"/qr/orders/{code}.svg\"", await customer.GetStringAsync($"/account/orders/{code}"));

        var admin = await AdminAsync();
        var details = await admin.GetStringAsync($"/admin/orders/details/{orderId}");
        Assert.Contains($"{BaseUrl}/q/o/{code}", details);
        Assert.Contains($"/admin/orders/print/{orderId}", details);

        var slip = await admin.GetStringAsync($"/admin/orders/print/{orderId}");
        Assert.Contains($"data-order-slip=\"{code}\"", slip);
        Assert.Contains($"src=\"/qr/orders/{code}.svg\"", slip);
        Assert.Contains("Thu hộ (COD)", slip);

        var mail = Directory.GetFiles(factory.EmailPickupDirectory).Select(File.ReadAllText).Single(m => m.Contains(code) && m.Contains("Cảm ơn bạn đã đặt hàng"));
        Assert.Contains($"{BaseUrl}/qr/orders/{code}.png?scale=5", mail);
    }

    [Theory]
    [InlineData("/admin/products/qrlabels")]
    [InlineData("/admin/orders/print/1")]
    public async Task PrintPages_AreAdminOnly(string page)
    {
        var anonymous = await factory.CreateClient().GetAsync(page);
        Assert.Contains("/account/login", anonymous.Headers.Location!.ToString());

        var customer = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword);
        var denied = await customer.GetAsync(page);
        Assert.StartsWith("https://localhost/account/access-denied", denied.Headers.Location!.ToString());
    }

    [Fact]
    public async Task QrAddresses_AreKeptOutOfSearchEngines()
    {
        var robots = await factory.CreateClient().GetStringAsync("/robots.txt");

        Assert.Contains("Disallow: /q/", robots);
        Assert.Contains("Disallow: /qr/", robots);
    }

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMetaRegex();

    [GeneratedRegex("data-qr-label=\"\\d+\"")]
    private static partial Regex QrLabelRegex();
}
