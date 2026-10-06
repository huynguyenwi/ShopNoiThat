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

/// <summary>Province → ward pickers backed by /api/locations, no district field, and ticking cart lines.</summary>
public sealed partial class AddressAndCartSelectionWebTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static async Task<string> CsrfAsync(HttpClient client) =>
        WebUtility.HtmlDecode(CsrfMetaRegex().Match(await client.GetStringAsync("/")).Groups[1].Value);

    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string url, object body, string csrf)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private async Task<int> VariantIdAsync(string sku) =>
        await DbAsync(db => db.ProductVariants.Where(v => v.Product.Sku == sku && v.StockQuantity >= 3).Select(v => v.Id).FirstAsync());

    // ------------------------------------------------------------------ /api/locations

    [Fact]
    public async Task LocationsApi_ListsProvinces_ThenTheWardsOfOne()
    {
        var client = factory.CreateClient();

        var provinces = await client.GetAsync("/api/locations/provinces");
        Assert.Equal(HttpStatusCode.OK, provinces.StatusCode);
        Assert.Contains("max-age=86400", provinces.Headers.CacheControl?.ToString());
        var list = (await provinces.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(34, list.Count);
        var hcm = list.Single(p => p.GetProperty("name").GetString() == "TP. Hồ Chí Minh");

        var wards = await client.GetFromJsonAsync<JsonElement>($"/api/locations/provinces/{hcm.GetProperty("code").GetInt32()}/wards");
        var names = wards.GetProperty("data").EnumerateArray().Select(w => w.GetProperty("name").GetString()).ToList();
        Assert.Equal(hcm.GetProperty("wardCount").GetInt32(), names.Count);
        Assert.Contains("Phường Sài Gòn", names);
        var first = wards.GetProperty("data")[0];
        Assert.Equal("Phường", first.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("label").GetString()));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/locations/provinces/999/wards")).StatusCode);
    }

    // ------------------------------------------------------------------ address forms

    [Fact]
    public async Task ContactOrderPage_HasProvinceAndWardPickers_AndNoDistrict()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);
        await SendJsonAsync(client, HttpMethod.Post, "/api/cart", new { variantId = await VariantIdAsync("GA-CURVE"), quantity = 1 }, await CsrfAsync(client));

        var page = await client.GetStringAsync("/checkout");

        Assert.DoesNotContain("Command.District", page);
        Assert.DoesNotContain("Quận / Huyện", page);
        Assert.Contains("data-province-select", page);
        Assert.Contains("<option value=\"TP. Hồ Chí Minh\" data-code=\"79\"", page);
        Assert.Matches("<select id=\"Command_Ward\" name=\"Command.Ward\"[^>]*disabled", page);   // until a province is chosen
        Assert.Contains("/js/address-picker.js", page);
    }

    [Fact]
    public async Task AWardOfAnotherProvince_IsRefused_AndTheFormKeepsTheChosenProvincesWards()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);
        await SendJsonAsync(client, HttpMethod.Post, "/api/cart", new { variantId = await VariantIdAsync("GA-CURVE"), quantity = 1 }, await CsrfAsync(client));
        var fields = new Dictionary<string, string>
        {
            ["Command.FullName"] = "Khách Địa Chỉ", ["Command.Phone"] = "0912345678", ["Command.Email"] = "dc@example.com",
            ["Command.Province"] = "TP. Hà Nội", ["Command.Ward"] = "Phường Sài Gòn", ["Command.AddressLine"] = "1 Tràng Tiền"
        };

        var refused = await PostFormAsync(client, "/checkout", "/checkout", fields);
        var html = await refused.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);            // the form again, not an order
        Assert.Contains("Phường / xã này không thuộc TP. Hà Nội", html);
        Assert.Contains("<option value=\"Phường Hoàn Kiếm\">Hoàn Kiếm</option>", html);   // Hà Nội's wards are offered
        Assert.Contains("“Phường Sài Gòn” không có trong danh sách phường / xã của TP. Hà Nội", html);

        fields["Command.Ward"] = "Phường Hoàn Kiếm";
        var placed = await PostFormAsync(client, "/checkout", "/checkout", fields);
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        Assert.Null(await DbAsync(db => db.OrderAddresses.Where(a => a.Ward == "Phường Hoàn Kiếm" && a.RecipientName == "Khách Địa Chỉ").Select(a => a.District).SingleAsync()));
    }

    [Fact]
    public async Task AddressBook_HasNoDistrict_AndValidatesTheWard()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);

        var page = await client.GetStringAsync("/account/addresses");
        Assert.DoesNotContain("Command.District", page);
        Assert.Contains("data-ward-select", page);

        var refused = await PostFormAsync(client, "/account/addresses", "/account/addresses", new Dictionary<string, string>
        {
            ["Command.RecipientName"] = "A", ["Command.Phone"] = "0912345678", ["Command.Province"] = "Khánh Hòa",
            ["Command.Ward"] = "Phường Sài Gòn", ["Command.AddressLine"] = "1 Trần Phú"
        });
        Assert.Contains("không thuộc Khánh Hòa", await refused.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------ ticking cart lines

    [Fact]
    public async Task CartLines_CanBeTicked_AndOnlyTickedOnesGoToTheContactOrderPage()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);
        var csrf = await CsrfAsync(client);
        await SendJsonAsync(client, HttpMethod.Post, "/api/cart", new { variantId = await VariantIdAsync("GA-CURVE"), quantity = 1 }, csrf);
        var added = await SendJsonAsync(client, HttpMethod.Post, "/api/cart", new { variantId = await VariantIdAsync("BBA-ANGIA"), quantity = 1 }, csrf);
        var items = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.All(items, i => Assert.True(i.GetProperty("isSelected").GetBoolean()));
        var chairLine = items.Single(i => i.GetProperty("productName").GetString()!.StartsWith("Ghế")).GetProperty("itemId").GetInt32();

        var cartPage = await client.GetStringAsync("/cart");
        Assert.Contains("data-cart-select-all", cartPage);
        Assert.Contains($"data-cart-select=\"{chairLine}\"", cartPage);
        Assert.Contains("Liên hệ đặt hàng (2)", cartPage);

        // API: untick the chair
        var unticked = await SendJsonAsync(client, HttpMethod.Put, $"/api/cart/items/{chairLine}/selected", new { selected = false }, csrf);
        Assert.Equal(HttpStatusCode.OK, unticked.StatusCode);
        var content = await client.GetStringAsync("/cart/content");
        Assert.Contains("Liên hệ đặt hàng (1)", content);
        Assert.Contains("Sản phẩm không chọn vẫn được giữ trong giỏ hàng.", content);
        Assert.DoesNotContain("<html", content);                       // the partial alone

        var checkout = await client.GetStringAsync("/checkout");
        Assert.Contains("Bộ bàn ăn gỗ sồi Nga An Gia", checkout);
        Assert.DoesNotContain("Ghế ăn gỗ sồi lưng cong", checkout);
        Assert.Contains("1 sản phẩm khác vẫn ở trong", checkout);

        // Without JavaScript: untick everything with the plain form, the contact-order page sends back to the cart.
        var noneTicked = await PostFormAsync(client, "/cart", "/cart/select-all", new Dictionary<string, string> { ["selected"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, noneTicked.StatusCode);
        var toCart = await client.GetAsync("/checkout");
        Assert.Equal(HttpStatusCode.Redirect, toCart.StatusCode);
        Assert.Equal("/cart", toCart.Headers.Location!.ToString());
        Assert.Contains("Chọn sản phẩm để đặt hàng", await client.GetStringAsync("/cart"));
    }

    [Fact]
    public async Task SelectionApi_ValidatesInput_AndOnlyTouchesTheOwnCart()
    {
        var owner = factory.CreateClient();
        await RegisterAsync(owner);
        var csrf = await CsrfAsync(owner);
        var added = await SendJsonAsync(owner, HttpMethod.Post, "/api/cart", new { variantId = await VariantIdAsync("GA-CURVE"), quantity = 1 }, csrf);
        var line = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("items")[0].GetProperty("itemId").GetInt32();

        Assert.Equal(HttpStatusCode.BadRequest, (await SendJsonAsync(owner, HttpMethod.Put, $"/api/cart/items/{line}/selected", new { }, csrf)).StatusCode);

        var stranger = factory.CreateClient();
        await RegisterAsync(stranger);
        await SendJsonAsync(stranger, HttpMethod.Post, "/api/cart", new { variantId = await VariantIdAsync("GA-CURVE"), quantity = 1 }, await CsrfAsync(stranger));
        var foreign = await SendJsonAsync(stranger, HttpMethod.Put, $"/api/cart/items/{line}/selected", new { selected = false }, await CsrfAsync(stranger));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.True(await DbAsync(db => db.CartItems.Where(i => i.Id == line).Select(i => i.IsSelected).SingleAsync()));

        var noCsrf = await owner.PutAsJsonAsync($"/api/cart/items/{line}/selected", new { selected = false });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    [Fact]
    public async Task OrderingFromTheProductPage_SendsOnlyThatProduct()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);
        var csrf = await CsrfAsync(client);
        await SendJsonAsync(client, HttpMethod.Post, "/api/cart", new { variantId = await VariantIdAsync("GA-CURVE"), quantity = 1 }, csrf);

        var buyNow = await SendJsonAsync(client, HttpMethod.Post, "/api/cart", new { variantId = await VariantIdAsync("BBA-ANGIA"), quantity = 1, buyNow = true }, csrf);

        var items = (await buyNow.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(["Bộ bàn ăn gỗ sồi Nga An Gia"], items.Where(i => i.GetProperty("isSelected").GetBoolean()).Select(i => i.GetProperty("productName").GetString()));
    }

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMetaRegex();
}
