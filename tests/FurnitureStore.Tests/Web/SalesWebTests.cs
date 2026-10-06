using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

public sealed partial class SalesWebTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private async Task<int> InStockVariantIdAsync(string productSku = "GA-CURVE")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ProductVariants.Where(v => v.Product.Sku == productSku && v.StockQuantity >= 3).Select(v => v.Id).FirstAsync();
    }

    private static async Task<string> CsrfAsync(HttpClient client, string page = "/")
    {
        var html = await client.GetStringAsync(page);
        return WebUtility.HtmlDecode(CsrfMetaRegex().Match(html).Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body, string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task GuestCart_ThenLogin_MergesCart_ThenCheckoutAndCancelThroughTheUi()
    {
        var client = factory.CreateClient();
        var variantId = await InStockVariantIdAsync();

        // 1. Guest adds to cart through the API (anonymous cart cookie is issued).
        var add = await PostJsonAsync(client, "/api/cart", new { variantId, quantity = 2 }, await CsrfAsync(client));
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        Assert.Contains(add.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".NhaMoc.Cart=") && c.Contains("httponly"));
        var cartPage = await client.GetStringAsync("/cart");
        Assert.Contains("Ghế ăn gỗ sồi lưng cong", cartPage);

        // 2. Checkout requires sign-in.
        var checkoutAnonymous = await client.GetAsync("/checkout");
        Assert.Equal(HttpStatusCode.Redirect, checkoutAnonymous.StatusCode);
        Assert.Contains("/account/login", checkoutAnonymous.Headers.Location!.ToString());

        // 3. Register → the guest cart follows the new account.
        await RegisterAsync(client);
        var count = await client.GetFromJsonAsync<JsonElement>("/api/cart/count");
        Assert.Equal(2, count.GetProperty("data").GetProperty("count").GetInt32());

        // 4. Checkout form.
        var checkoutPage = await client.GetStringAsync("/checkout");
        Assert.Contains("TP. Hồ Chí Minh", checkoutPage);
        var placed = await PostFormAsync(client, "/checkout", "/checkout", new Dictionary<string, string>
        {
            ["Command.FullName"] = "Khách Thử Nghiệm",
            ["Command.Phone"] = "0912345678",
            ["Command.Email"] = "khach-ui@example.com",
            ["Command.Province"] = "TP. Hà Nội",
            ["Command.Ward"] = "Phường Hoàn Kiếm",
            ["Command.AddressLine"] = "1 Tràng Tiền",
            ["Command.PaymentMethod"] = "BankTransfer",
            ["Command.SaveAddress"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        var successUrl = placed.Headers.Location!.ToString();
        Assert.Matches("/checkout/success/DH\\d{6}-[A-Z2-9]{5}$", successUrl);

        var success = await client.GetStringAsync(successUrl);
        var code = successUrl.Split('/').Last();
        Assert.Contains(code, success);
        Assert.Contains("Thông tin chuyển khoản", success);
        Assert.Contains($"NHAMOC {code}", success);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/cart/count")).GetProperty("data").GetProperty("count").GetInt32());

        // 5. The order appears in "Đơn hàng của tôi" and can be cancelled.
        Assert.Contains(code, await client.GetStringAsync("/account/orders"));
        var cancel = await PostFormAsync(client, $"/account/orders/{code}", $"/account/orders/{code}/cancel", new Dictionary<string, string> { ["reason"] = "Đặt nhầm" });
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        var detail = await client.GetStringAsync($"/account/orders/{code}");
        Assert.Contains("Đã hủy", detail);
        Assert.Contains("Đặt nhầm", detail);
    }

    [Fact]
    public async Task CartApi_ValidatesInput_AndStock()
    {
        var client = factory.CreateClient();
        var csrf = await CsrfAsync(client);
        var variantId = await InStockVariantIdAsync();

        var zero = await PostJsonAsync(client, "/api/cart", new { variantId, quantity = 0 }, csrf);
        var tooMany = await PostJsonAsync(client, "/api/cart", new { variantId, quantity = 99 }, csrf);
        var missing = await PostJsonAsync(client, "/api/cart", new { variantId = 999_999, quantity = 1 }, csrf);

        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("chỉ còn", (await tooMany.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task CartApi_WithoutCsrfHeader_IsRejected()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/cart", new { variantId = await InStockVariantIdAsync(), quantity = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OrdersApi_RequiresSignIn_AndHidesOtherCustomersOrders()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/orders")).StatusCode);

        // Customer A places an order through the API.
        var a = factory.CreateClient();
        await RegisterAsync(a);
        var csrf = await CsrfAsync(a);
        await PostJsonAsync(a, "/api/cart", new { variantId = await InStockVariantIdAsync("KTT-PINE"), quantity = 1 }, csrf);
        var placed = await PostJsonAsync(a, "/api/orders", new
        {
            fullName = "Khách A", phone = "0912345678", email = "a@example.com", addressLine = "5 Hai Bà Trưng",
            ward = "Phường Sài Gòn", province = "TP. Hồ Chí Minh", paymentMethod = "COD"
        }, csrf);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        var orderId = (await placed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("orderId").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/api/orders/{orderId}")).StatusCode);

        // Customer B cannot read it.
        var b = factory.CreateClient();
        await RegisterAsync(b);
        var forbidden = await b.GetAsync($"/api/orders/{orderId}");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        Assert.Equal(0, (await b.GetFromJsonAsync<JsonElement>("/api/orders")).GetProperty("data").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Wishlist_AnonymousGets401_SignedInCanToggle()
    {
        var anonymous = factory.CreateClient();
        var anonymousToggle = new HttpRequestMessage(HttpMethod.Post, "/api/wishlist/1/toggle");
        anonymousToggle.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(anonymous));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(anonymousToggle)).StatusCode);

        var client = factory.CreateClient();
        await RegisterAsync(client);
        var productId = (await client.GetFromJsonAsync<JsonElement>("/api/products?pageSize=1")).GetProperty("data").GetProperty("items")[0].GetProperty("id").GetInt32();
        var toggle = await PostJsonAsync(client, $"/api/wishlist/{productId}/toggle", new { }, await CsrfAsync(client));

        Assert.True((await toggle.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("inWishlist").GetBoolean());
        Assert.Contains($"data-wishlist-product=\"{productId}\"", await client.GetStringAsync("/wishlist"));
    }

    [Fact]
    public async Task EveryPostFormOnKeyPages_CarriesAnAntiforgeryToken()
    {
        // Regression: <form action="..."> without asp-* attributes does not get a token automatically.
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        await PostJsonAsync(client, "/api/cart", new { variantId = await InStockVariantIdAsync(), quantity = 1 }, await CsrfAsync(client));

        foreach (var page in new[] { "/", "/cart", "/account/profile", "/account/addresses", "/admin", "/admin/attributes/colors" })
        {
            var html = await client.GetStringAsync(page);
            foreach (Match form in PostFormRegex().Matches(html))
            {
                Assert.True(form.Value.Contains("__RequestVerificationToken"), $"POST form without anti-forgery token on {page}: {form.Value[..Math.Min(150, form.Value.Length)]}");
            }
        }
    }

    [Fact]
    public async Task HeaderLogoutForm_ActuallyLogsOut()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);
        var home = await client.GetStringAsync("/");
        var logoutForm = LogoutFormRegex().Match(home).Value;
        var token = WebUtility.HtmlDecode(TokenRegex().Match(logoutForm).Groups[1].Value);

        var response = await client.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/account/profile")).StatusCode);
    }

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMetaRegex();

    [GeneratedRegex("<form[^>]*method=\"post\"[^>]*>.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PostFormRegex();

    [GeneratedRegex("<form method=\"post\" action=\"/account/logout\".*?</form>", RegexOptions.Singleline)]
    private static partial Regex LogoutFormRegex();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
