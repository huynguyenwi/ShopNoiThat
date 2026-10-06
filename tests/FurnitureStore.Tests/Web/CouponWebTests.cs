using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

/// <summary>/admin/coupons pages, coupon offers in the cart and the coupon box of the checkout page.</summary>
public sealed partial class CouponWebTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private Task<HttpClient> AdminAsync() =>
        CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static async Task<string> CsrfAsync(HttpClient client, string page = "/")
    {
        var html = await client.GetStringAsync(page);
        return WebUtility.HtmlDecode(CsrfMetaRegex().Match(html).Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(client));
        return await client.SendAsync(request);
    }

    /// <summary>Signed-in customer with a chair (about 1.9 million) in the cart.</summary>
    private async Task<HttpClient> CustomerWithCartAsync()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);
        var variantId = await DbAsync(db => db.ProductVariants.Where(v => v.Product.Sku == "GA-CURVE" && v.StockQuantity >= 3).Select(v => v.Id).FirstAsync());
        var add = await SendJsonAsync(client, HttpMethod.Post, "/api/cart", new { variantId, quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        return client;
    }

    private async Task<int> CreateCouponAsync(HttpClient admin, string code, bool isPublic = true)
    {
        var response = await PostFormAsync(admin, "/admin/coupons/create", "/admin/coupons/create", new Dictionary<string, string>
        {
            ["Command.Code"] = code.ToLowerInvariant(),
            ["Command.Name"] = "Giảm 200.000đ thử nghiệm",
            ["Command.Description"] = "",
            ["Command.DiscountType"] = "FixedAmount",
            ["Command.DiscountValue"] = "200000",
            ["Command.MaxDiscountAmount"] = "",
            ["Command.MinOrderAmount"] = "",
            ["Command.UsageLimit"] = "",
            ["Command.UsageLimitPerUser"] = "",
            ["Command.StartsAt"] = "",
            ["Command.EndsAt"] = "",
            ["Command.IsActive"] = "true",
            ["Command.IsPublic"] = isPublic ? "true" : "false"
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var match = CouponUrlRegex().Match(response.Headers.Location!.ToString());
        Assert.True(match.Success, response.Headers.Location!.ToString());
        return int.Parse(match.Groups[1].Value);
    }

    // ------------------------------------------------------------------ Admin pages

    [Theory]
    [InlineData("/admin/coupons")]
    [InlineData("/admin/coupons/create")]
    [InlineData("/admin/coupons/1")]
    [InlineData("/admin/coupons/1/edit")]
    public async Task CouponPages_AreAdminOnly(string page)
    {
        var anonymous = await factory.CreateClient().GetAsync(page);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/account/login", anonymous.Headers.Location!.ToString());

        var customer = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword);
        var denied = await customer.GetAsync(page);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("https://localhost/account/access-denied", denied.Headers.Location!.ToString());

        var admin = await AdminAsync();
        var ok = await admin.GetAsync(page);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var html = await ok.Content.ReadAsStringAsync();
        foreach (Match form in PostFormRegex().Matches(html))
        {
            Assert.Contains("__RequestVerificationToken", form.Value);
        }
    }

    [Fact]
    public async Task CouponPosts_WithoutAntiforgeryToken_AreRejected()
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsync("/admin/coupons/1/delete", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await DbAsync(db => db.Coupons.AnyAsync(c => c.Id == 1)));
    }

    [Fact]
    public async Task ListAndDetail_ShowTheCoupon_WithItsStatus()
    {
        var admin = await AdminAsync();
        var id = await CreateCouponAsync(admin, "WEBLIST");

        var list = await admin.GetStringAsync("/admin/coupons?status=Running&search=weblist");
        Assert.Contains("WEBLIST", list);
        Assert.Contains("Đang chạy", list);
        Assert.DoesNotContain("CHAOBAN10", list);

        var detail = await admin.GetStringAsync($"/admin/coupons/{id}");
        Assert.Contains("Giảm 200.000₫", detail);
        Assert.Contains("Chưa có đơn hàng nào dùng mã này.", detail);

        var toggled = await PostFormAsync(admin, $"/admin/coupons/{id}", $"/admin/coupons/{id}/active",
            new Dictionary<string, string> { ["active"] = "false", ["returnUrl"] = "https://evil.example/" });
        Assert.Equal("/admin/coupons", toggled.Headers.Location!.ToString()); // no open redirect
        Assert.Contains("data-coupon-row=\"WEBLIST\"", await admin.GetStringAsync("/admin/coupons?status=Inactive&search=weblist"));
        Assert.DoesNotContain("data-coupon-row=\"WEBLIST\"", await admin.GetStringAsync("/admin/coupons?status=Running&search=weblist"));
    }

    [Fact]
    public async Task InvalidForm_IsShownAgain_WithVietnameseFieldErrors()
    {
        var admin = await AdminAsync();

        var response = await PostFormAsync(admin, "/admin/coupons/create", "/admin/coupons/create", new Dictionary<string, string>
        {
            ["Command.Code"] = "giảm giá",
            ["Command.Name"] = "",
            ["Command.DiscountType"] = "Percentage",
            ["Command.DiscountValue"] = "150",
            ["Command.IsActive"] = "true",
            ["Command.IsPublic"] = "false"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Mã chỉ gồm chữ không dấu", html);
        Assert.Contains("Vui lòng nhập tên chương trình.", html);
        Assert.Contains("Phần trăm giảm phải từ 1 đến 100.", html);
    }

    // ------------------------------------------------------------------ Customer side

    [Fact]
    public async Task PublicCoupon_IsOfferedInTheCart_AndAppliesWithOneClick()
    {
        var admin = await AdminAsync();
        await CreateCouponAsync(admin, "WEBOFFER");
        await CreateCouponAsync(admin, "WEBPRIVATE", isPublic: false);
        var customer = await CustomerWithCartAsync();

        var cart = await customer.GetStringAsync("/cart");
        Assert.Contains("Ưu đãi dành cho bạn", cart);
        Assert.Contains("data-coupon-offer=\"WEBOFFER\"", cart);
        Assert.Contains("value=\"WEBOFFER\"", cart);                     // one-click apply form
        Assert.DoesNotContain("WEBPRIVATE", cart);                        // private codes are typed, not listed
        Assert.Contains("data-coupon-offer=\"CHAOBAN10\"", cart);
        Assert.Contains("Mua thêm", cart);                                // not reached yet: 5 million minimum

        var apply = await PostFormAsync(customer, "/cart", "/cart/coupon", new Dictionary<string, string> { ["code"] = "WEBOFFER", ["returnUrl"] = "/cart" });
        Assert.Equal("/cart", apply.Headers.Location!.ToString());
        var after = await customer.GetStringAsync("/cart");
        Assert.Contains("data-coupon-applied=\"WEBOFFER\"", after);
        Assert.Contains("-200.000₫", after);

        // A private code works when typed.
        var typed = await SendJsonAsync(customer, HttpMethod.Post, "/api/cart/coupon", new { code = "webprivate" });
        Assert.Equal(HttpStatusCode.OK, typed.StatusCode);
    }

    [Fact]
    public async Task Checkout_CouponBoxIsOutsideTheOrderForm_AndItsSummaryReloads()
    {
        var admin = await AdminAsync();
        await CreateCouponAsync(admin, "WEBCHECKOUT");
        var customer = await CustomerWithCartAsync();

        var page = await customer.GetStringAsync("/checkout");
        var orderForm = CheckoutFormRegex().Match(page).Value;
        Assert.NotEmpty(orderForm);
        Assert.DoesNotContain("/cart/coupon", orderForm);                // no nested forms
        Assert.Contains("form=\"checkoutForm\"", page);                    // the order button still submits the order form
        Assert.Contains("data-coupon-offer=\"WEBCHECKOUT\"", page);
        Assert.Contains("name=\"returnUrl\" value=\"/checkout\"", page);

        // Without JavaScript: the posted coupon form comes back to the checkout page.
        var apply = await PostFormAsync(customer, "/checkout", "/cart/coupon", new Dictionary<string, string> { ["code"] = "WEBCHECKOUT", ["returnUrl"] = "/checkout" });
        Assert.Equal("/checkout", apply.Headers.Location!.ToString());

        // With JavaScript: only the summary column is reloaded.
        var summary = await customer.GetAsync("/checkout/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        var html = await summary.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<html", html);
        Assert.Contains("Giảm giá (WEBCHECKOUT)", html);
        Assert.Contains("data-coupon-applied=\"WEBCHECKOUT\"", html);
        Assert.Contains("__RequestVerificationToken", html);

        var removed = await SendJsonAsync(customer, HttpMethod.Delete, "/api/cart/coupon");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.DoesNotContain("Giảm giá (WEBCHECKOUT)", await customer.GetStringAsync("/checkout/summary"));

        // Only /checkout is accepted as a return address.
        var evil = await PostFormAsync(customer, "/cart", "/cart/coupon", new Dictionary<string, string> { ["code"] = "WEBCHECKOUT", ["returnUrl"] = "https://evil.example/" });
        Assert.Equal("/cart", evil.Headers.Location!.ToString());
    }

    [Fact]
    public async Task CheckoutSummary_RequiresSignIn()
    {
        var response = await factory.CreateClient().GetAsync("/checkout/summary");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.ToString());
    }

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMetaRegex();

    [GeneratedRegex("^/admin/coupons/(\\d+)$")]
    private static partial Regex CouponUrlRegex();

    [GeneratedRegex("<form[^>]*method=\"post\"[\\s\\S]*?</form>", RegexOptions.IgnoreCase)]
    private static partial Regex PostFormRegex();

    [GeneratedRegex("<form[^>]*id=\"checkoutForm\"[\\s\\S]*?</form>")]
    private static partial Regex CheckoutFormRegex();
}
