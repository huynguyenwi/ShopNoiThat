using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using FurnitureStore.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

/// <summary>
/// Phase 11 regressions: every API error uses the ApiResponse envelope (never ProblemDetails), stale anti-forgery
/// tokens get an actionable message, and no framework English / parser details reach users.
/// </summary>
public sealed partial class ErrorResponseTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private static async Task<string> CsrfAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/");
        return WebUtility.HtmlDecode(CsrfMetaRegex().Match(html).Groups[1].Value);
    }

    private static async Task<JsonElement> EnvelopeAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
        return body;
    }

    [Fact]
    public async Task Api_WithoutAntiforgeryToken_ReturnsEnvelope_AskingToReload()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/cart", new { variantId = 1, quantity = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await EnvelopeAsync(response);
        Assert.Equal(AntiforgeryFailureFilter.Message, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Form_WithoutAntiforgeryToken_ShowsErrorPage_AskingToReload()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/cart/coupon", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = "GIAM500K" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Phiên làm việc đã hết hạn", html);
    }

    [Theory]
    [InlineData("{\"variantId\": \"abc\",")]          // unparsable JSON
    [InlineData("{\"variantId\": \"abc\", \"quantity\": 1}")] // wrong type
    [InlineData("")]                                   // no body
    public async Task Api_MalformedJson_ReturnsVietnameseEnvelope_WithoutParserDetails(string json)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cart") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(client));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("System.", raw);
        Assert.DoesNotContain("JSON", raw);
        var body = await EnvelopeAsync(response);
        Assert.Contains(ValidationMessages.MalformedBody, body.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Api_UnsupportedContentType_ReturnsEnvelope()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cart") { Content = new StringContent("hello", Encoding.UTF8, "text/plain") };
        request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(client));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var body = await EnvelopeAsync(response);
        Assert.Equal("Định dạng dữ liệu không được hỗ trợ.", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("/admin/products/create")]
    [InlineData("/admin/categories/create")]
    [InlineData("/admin/ai-knowledge/create")]
    [InlineData("/admin/price-rules/create")]
    [InlineData("/admin/store")]
    [InlineData("/admin/coupons/create")]
    [InlineData("/admin/coupons/1/edit")]
    public async Task AdminForms_ClientValidationMessages_AreVietnamese(string page)
    {
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

        var html = await client.GetStringAsync(page);

        var english = EnglishValidationRegex().Matches(html).Select(m => m.Value).ToList();
        Assert.True(english.Count == 0, $"{page}: {string.Join(" | ", english)}");
    }

    [Fact]
    public async Task CustomerForms_ClientValidationMessages_AreVietnamese()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);

        foreach (var page in new[] { "/account/addresses", "/account/profile", "/bao-gia", "/contact" })
        {
            var html = await client.GetStringAsync(page);
            var english = EnglishValidationRegex().Matches(html).Select(m => m.Value).ToList();
            Assert.True(english.Count == 0, $"{page}: {string.Join(" | ", english)}");
        }
    }

    [Fact]
    public async Task NumberFields_UseVietnameseMessage()
    {
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

        var html = await client.GetStringAsync("/admin/products/create");

        Assert.Contains($"data-val-number=\"{ValidationMessages.MustBeNumber}\"", html);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public async Task FormBindingError_ShowsTheFormAgain_InsteadOfSavingADefault(string displayOrder)
    {
        // Regression: "abc" / "" in a number field used to be saved silently as 0.
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        var name = $"Danh mục lỗi số {Guid.NewGuid():N}";

        var response = await PostFormAsync(client, "/admin/categories/create", "/admin/categories/create", new Dictionary<string, string>
        {
            ["Command.Name"] = name,
            ["Command.DisplayOrder"] = displayOrder,
            ["Command.IsActive"] = "true"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // form shown again
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(BindingGuard.Summary, html);
        var input = DisplayOrderInputRegex().Match(html).Value;
        Assert.Contains("input-validation-error", input); // the faulty field is marked
        Assert.Contains($"value=\"{displayOrder}\"", input); // and keeps what was typed
        Assert.DoesNotContain("is not valid", html);
        Assert.Equal(0, await DbAsync(db => db.Categories.CountAsync(c => c.Name == name)));
    }

    [Fact]
    public async Task CartQuantity_NotANumber_KeepsTheLine()
    {
        var client = factory.CreateClient();
        var variantId = await DbAsync(db => db.ProductVariants.Where(v => v.IsActive && v.StockQuantity >= 2).Select(v => v.Id).FirstAsync());
        var add = new HttpRequestMessage(HttpMethod.Post, "/api/cart") { Content = JsonContent.Create(new { variantId, quantity = 2 }) };
        add.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(client));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(add)).StatusCode);
        var cartPage = await client.GetStringAsync("/cart");
        var itemId = CartQuantityActionRegex().Match(cartPage).Groups[1].Value;

        var response = await PostFormAsync(client, "/cart", $"/cart/items/{itemId}/quantity", new Dictionary<string, string> { ["quantity"] = "abc" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var count = await client.GetFromJsonAsync<JsonElement>("/api/cart/count");
        Assert.Equal(2, count.GetProperty("data").GetProperty("count").GetInt32());
        Assert.Contains(BindingGuard.Summary, await client.GetStringAsync("/cart"));
    }

    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    [GeneratedRegex("<input[^>]*id=\"Command_DisplayOrder\"[^>]*>")]
    private static partial Regex DisplayOrderInputRegex();

    [GeneratedRegex("action=\"/cart/items/(\\d+)/quantity\"")]
    private static partial Regex CartQuantityActionRegex();

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMetaRegex();

    [GeneratedRegex("data-val-[a-z]+=\"[^\"]*(field is required|must be a number|is not valid|The field|is invalid)[^\"]*\"")]
    private static partial Regex EnglishValidationRegex();
}
