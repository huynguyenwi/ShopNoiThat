using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

/// <summary>Phase 9 through HTTP: estimate, quote requests, customer and admin workflow, price rules.</summary>
public sealed partial class QuoteWebTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private const string WalnutTable = "Tôi muốn bàn ăn gỗ óc chó dài 1m8 rộng 90cm cao 75cm, sơn PU";

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body)
    {
        var html = await client.GetStringAsync("/");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", WebUtility.HtmlDecode(CsrfMetaRegex().Match(html).Groups[1].Value));
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean(), json.ToString());
        return json.GetProperty("data");
    }

    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    [Fact]
    public async Task QuotePage_Renders_AndPrefillsFromQuery()
    {
        var html = await factory.CreateClient().GetStringAsync("/bao-gia?q=" + Uri.EscapeDataString("giường gỗ sồi 1m6"));

        Assert.Contains("id=\"quoteParseForm\"", html);
        Assert.Contains("giường gỗ sồi 1m6</textarea>", html);
        Assert.Contains("quote.js", html);
        Assert.Contains("value=\"ban-an\"", html);
    }

    [Fact]
    public async Task PriceEstimate_ComesFromTheCalculator()
    {
        var client = factory.CreateClient();

        var data = await DataAsync(await PostJsonAsync(client, "/api/ai/price-estimate", new { text = WalnutTable }));

        Assert.Equal(17_800_000m, data.GetProperty("breakdown").GetProperty("unitPrice").GetDecimal());
        Assert.Equal("Bàn ăn", data.GetProperty("spec").GetProperty("typeName").GetString());
        Assert.Equal("PU", data.GetProperty("spec").GetProperty("finish").GetString());
        Assert.Contains("17.800.000đ", data.GetProperty("explanation").GetString());

        var unknown = await PostJsonAsync(client, "/api/ai/price-estimate", new { text = "làm giúp mình món gì đó" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("loại sản phẩm", await unknown.Content.ReadAsStringAsync());

        var noCsrf = await client.PostAsync("/api/ai/price-estimate", JsonContent.Create(new { text = WalnutTable }));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    [Fact]
    public async Task GuestQuote_PriceIsRecomputed_EvenIfTheClientSendsOne()
    {
        var response = await PostJsonAsync(factory.CreateClient(), "/api/quotes", new
        {
            text = WalnutTable, name = "Khách Vãng Lai", phone = "0987654321",
            unitPrice = 1, estimatedTotal = 1, finalQuotedPrice = 1 // not part of the contract: ignored
        });

        var data = await DataAsync(response);
        var code = data.GetProperty("code").GetString()!;
        var quote = await DbAsync(db => db.QuoteRequests.AsNoTracking().SingleAsync(q => q.QuoteCode == code));
        Assert.Equal(17_800_000m, quote.EstimatedTotal);
        Assert.Null(quote.FinalQuotedPrice);
        Assert.Null(quote.UserId);
    }

    [Fact]
    public async Task Quote_Workflow_CustomerSubmits_AdminQuotes_CustomerAccepts()
    {
        var customer = factory.CreateClient();
        await RegisterAsync(customer);
        var code = (await DataAsync(await PostJsonAsync(customer, "/api/quotes", new { text = "tủ quần áo gỗ sồi 1m6 cao 2m", name = "Chị Mai", phone = "0912345678" })))
            .GetProperty("code").GetString()!;
        var id = await DbAsync(db => db.QuoteRequests.Where(q => q.QuoteCode == code).Select(q => q.Id).SingleAsync());

        Assert.Contains(code, await customer.GetStringAsync("/account/quotes"));
        var mine = await customer.GetStringAsync($"/account/quotes/{code}");
        Assert.Contains("Hủy yêu cầu", mine);
        Assert.DoesNotContain("Đồng ý báo giá", mine);

        // Admin sets the official price.
        var admin = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        Assert.Contains(code, await admin.GetStringAsync("/admin/quotes"));
        var version = await DbAsync(db => db.QuoteRequests.Where(q => q.Id == id).Select(q => q.Version).SingleAsync());
        var update = await PostFormAsync(admin, $"/admin/quotes/{id}", $"/admin/quotes/{id}/update", new Dictionary<string, string>
        {
            ["Status"] = "Quoted", ["FinalQuotedPrice"] = "24500000", ["AdminNote"] = "Đã gồm lắp đặt", ["Version"] = version.ToString()
        });
        Assert.Equal(HttpStatusCode.Redirect, update.StatusCode);
        Assert.Equal(QuoteStatus.Quoted, await DbAsync(db => db.QuoteRequests.Where(q => q.Id == id).Select(q => q.Status).SingleAsync()));

        // The customer sees the price (not the internal note of the admin page) and accepts.
        var quoted = await customer.GetStringAsync($"/account/quotes/{code}");
        Assert.Contains("24.500.000", quoted);
        Assert.Contains("Đồng ý báo giá", quoted);
        var accept = await PostFormAsync(customer, $"/account/quotes/{code}", $"/account/quotes/{code}/accept", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, accept.StatusCode);
        Assert.Equal(QuoteStatus.Accepted, await DbAsync(db => db.QuoteRequests.Where(q => q.Id == id).Select(q => q.Status).SingleAsync()));

        // Another customer cannot see it.
        var other = factory.CreateClient();
        await RegisterAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/quotes/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/account/quotes/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/quotes")).StatusCode);
    }

    [Fact]
    public async Task AdminQuotePages_AreAdminOnly_AndCarryAntiforgeryTokens()
    {
        var code = (await DataAsync(await PostJsonAsync(factory.CreateClient(), "/api/quotes", new { text = "kệ tivi gỗ sồi dài 1m8", name = "A", phone = "0912345678" })))
            .GetProperty("code").GetString()!;
        var id = await DbAsync(db => db.QuoteRequests.Where(q => q.QuoteCode == code).Select(q => q.Id).SingleAsync());
        var ruleId = await DbAsync(db => db.PriceRules.Select(r => r.Id).FirstAsync());
        string[] pages = ["/admin/quotes", $"/admin/quotes/{id}", "/admin/price-rules", "/admin/price-rules?type=ProfitMarginPercent", "/admin/price-rules/create", $"/admin/price-rules/{ruleId}/edit"];

        var admin = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        var customer = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword);
        foreach (var page in pages)
        {
            var response = await admin.GetAsync(page);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{page}: {(int)response.StatusCode}");
            foreach (Match form in PostFormRegex().Matches(await response.Content.ReadAsStringAsync()))
            {
                Assert.Contains("__RequestVerificationToken", form.Value);
            }

            var denied = await customer.GetAsync(page);
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("/account/access-denied", denied.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task Admin_EditsAPriceRule_AndTheNextEstimateUsesIt()
    {
        var admin = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        var rule = await DbAsync(db => db.PriceRules.AsNoTracking().SingleAsync(r => r.Code == "ACC-TABLE"));
        var page = $"/admin/price-rules/{rule.Id}/edit";

        var invalid = await PostFormAsync(admin, page, page, Fields(rule, value: "-5"));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Giá trị không được âm.", await invalid.Content.ReadAsStringAsync());

        var before = await DataAsync(await PostJsonAsync(factory.CreateClient(), "/api/ai/price-estimate", new { text = WalnutTable }));
        var saved = await PostFormAsync(admin, page, page, Fields(rule, value: "1300000"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var after = await DataAsync(await PostJsonAsync(factory.CreateClient(), "/api/ai/price-estimate", new { text = WalnutTable }));

        Assert.Equal(1_300_000m, after.GetProperty("breakdown").GetProperty("accessoryCost").GetDecimal());
        Assert.True(after.GetProperty("breakdown").GetProperty("unitPrice").GetDecimal() > before.GetProperty("breakdown").GetProperty("unitPrice").GetDecimal());

        // Restore for the other tests of this class.
        await PostFormAsync(admin, page, page, Fields(rule, value: "300000"));
    }

    [Fact]
    public async Task Chatbot_AnswersQuoteQuestions_WithTheCalculatorPrice()
    {
        var data = await DataAsync(await PostJsonAsync(factory.CreateClient(), "/api/ai/chat", new { message = "Báo giá giúp mình bàn ăn gỗ óc chó dài 1m8 rộng 90cm cao 75cm sơn PU" }));

        Assert.Contains("17.800.000đ", data.GetProperty("reply").GetString());
        Assert.Contains("/bao-gia", data.GetProperty("reply").GetString());
    }

    private static Dictionary<string, string> Fields(FurnitureStore.Domain.Entities.PriceRule rule, string value) => new()
    {
        ["Command.Code"] = rule.Code,
        ["Command.Name"] = rule.Name,
        ["Command.RuleType"] = rule.RuleType.ToString(),
        ["Command.FurnitureType"] = rule.FurnitureType?.ToString() ?? "",
        ["Command.MaterialId"] = rule.MaterialId?.ToString() ?? "",
        ["Command.FinishType"] = rule.FinishType?.ToString() ?? "",
        ["Command.Value"] = value,
        ["Command.Unit"] = rule.Unit,
        ["Command.Priority"] = rule.Priority.ToString(),
        ["Command.IsActive"] = "true"
    };

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMetaRegex();

    [GeneratedRegex("<form[^>]*method=\"post\"[^>]*>.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PostFormRegex();
}
