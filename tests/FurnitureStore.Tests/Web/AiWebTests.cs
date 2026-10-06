using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

/// <summary>Phase 8 through HTTP: AI API (rule-based mode, no key in tests), ownership, rate limit, pages and admin tools.</summary>
public sealed partial class AiWebTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private static async Task<string> CsrfAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/");
        return WebUtility.HtmlDecode(CsrfMetaRegex().Match(html).Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(client));
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

    // ------------------------------------------------------------------ API

    [Fact]
    public async Task Chat_AsGuest_ReturnsCatalogProducts_AndKeepsTheConversationPrivate()
    {
        var guest = factory.CreateClient();
        Assert.False((await DataAsync(await guest.GetAsync("/api/ai/status"))).GetProperty("aiEnabled").GetBoolean());

        var response = await PostJsonAsync(guest, "/api/ai/chat", new { message = "Tôi thích bàn gỗ màu nâu, dài khoảng 1m8" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".NhaMoc.Ai=") && c.Contains("httponly"));
        var data = await DataAsync(response);
        var products = data.GetProperty("products").EnumerateArray().ToList();
        Assert.NotEmpty(products);
        foreach (var product in products)
        {
            var id = product.GetProperty("productId").GetInt32();
            var dbPrice = await DbAsync(db => db.Products.Where(p => p.Id == id).Select(p => p.DiscountPrice ?? p.BasePrice).SingleAsync());
            Assert.Equal(dbPrice, product.GetProperty("price").GetDecimal());
            Assert.StartsWith("/products/", product.GetProperty("url").GetString());
        }

        var conversationId = data.GetProperty("conversationId").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/ai/conversations/{conversationId}")).StatusCode);

        var stranger = factory.CreateClient();
        var peek = await stranger.GetAsync($"/api/ai/conversations/{conversationId}");
        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
        var continueOther = await PostJsonAsync(stranger, "/api/ai/chat", new { conversationId, message = "tiếp tục" });
        Assert.Equal(HttpStatusCode.NotFound, continueOther.StatusCode);
    }

    [Fact]
    public async Task Chat_ValidatesInput_AndRequiresCsrf()
    {
        var client = factory.CreateClient();

        var empty = await PostJsonAsync(client, "/api/ai/chat", new { message = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("Vui lòng nhập câu hỏi.", await empty.Content.ReadAsStringAsync());

        var noCsrf = await client.PostAsync("/api/ai/chat", JsonContent.Create(new { message = "xin chào" }));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    [Fact]
    public async Task StructuredAdvice_Endpoints_Work()
    {
        var client = factory.CreateClient();

        var recommend = await DataAsync(await PostJsonAsync(client, "/api/ai/recommend", new { roomType = "Phòng khách", roomAreaM2 = 20, style = "Hiện đại", budget = 30_000_000 }));
        Assert.True(recommend.GetProperty("items").GetArrayLength() >= 2);
        Assert.True(recommend.GetProperty("totalPrice").GetDecimal() <= 31_500_000);

        var colors = await DataAsync(await PostJsonAsync(client, "/api/ai/color-recommend", new { wallColor = "trắng", furniture = "Bàn, Ghế" }));
        Assert.Equal(["Bàn", "Ghế"], colors.GetProperty("advice").EnumerateArray().Select(a => a.GetProperty("furniture").GetString()));

        var styles = await DataAsync(await PostJsonAsync(client, "/api/ai/style-recommend", new { roomAreaM2 = 12, budget = 10_000_000, preferences = "gọn gàng" }));
        Assert.Equal(3, styles.GetProperty("styles").GetArrayLength());

        var invalid = await PostJsonAsync(client, "/api/ai/recommend", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task AiApi_IsRateLimited()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("AI:RequestsPerMinute", "2"));
        var client = limited.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await PostJsonAsync(client, "/api/ai/chat", new { message = "sofa xám" })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        var blocked = await PostJsonAsync(client, "/api/ai/chat", new { message = "sofa xám" });
        Assert.Contains("quá nhiều yêu cầu", await blocked.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------ pages

    [Fact]
    public async Task Pages_ExposeTheAssistant()
    {
        var client = factory.CreateClient();

        var home = await client.GetStringAsync("/");
        Assert.Contains("id=\"aiWidget\"", home);
        Assert.Contains("ai-assistant.js", home);
        Assert.Contains("href=\"/tu-van\"", home);

        var advisor = await client.GetStringAsync("/tu-van");
        Assert.Contains("data-advisor-form=\"recommend\"", advisor);
        Assert.Contains("data-advisor-form=\"colors\"", advisor);
        Assert.Contains("data-advisor-form=\"styles\"", advisor);

        var slug = await DbAsync(db => db.Products.Select(p => p.Slug).FirstAsync());
        Assert.Contains("data-ai-product=", await client.GetStringAsync($"/products/{slug}"));
    }

    [Fact]
    public async Task GuestAiHistory_MovesToTheAccount_AtRegistration_AndIsPrivate()
    {
        var client = factory.CreateClient();
        var chat = await DataAsync(await PostJsonAsync(client, "/api/ai/chat", new { message = "kệ tivi gỗ óc chó" }));
        var conversationId = chat.GetProperty("conversationId").GetInt32();

        await RegisterAsync(client);

        var history = await client.GetStringAsync("/account/ai-history");
        Assert.Contains("kệ tivi gỗ óc chó", history);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/account/ai-history/{conversationId}")).StatusCode);

        var other = factory.CreateClient();
        await RegisterAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/account/ai-history/{conversationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await factory.CreateClient().GetAsync("/account/ai-history")).StatusCode);
    }

    // ------------------------------------------------------------------ admin

    [Fact]
    public async Task AdminAiPages_AreAdminOnly_AndCarryAntiforgeryTokens()
    {
        var guest = factory.CreateClient();
        var chat = await DataAsync(await PostJsonAsync(guest, "/api/ai/chat", new { message = "giường ngủ 1m6" }));
        var conversationId = chat.GetProperty("conversationId").GetInt32();
        var knowledgeId = await DbAsync(db => db.AIKnowledgeEntries.Select(k => k.Id).FirstAsync());
        string[] pages = ["/admin/ai", $"/admin/ai/{conversationId}", "/admin/ai-knowledge", "/admin/ai-knowledge/create", $"/admin/ai-knowledge/{knowledgeId}/edit"];

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

        var transcript = await admin.GetStringAsync($"/admin/ai/{conversationId}");
        Assert.Contains("giường ngủ 1m6", transcript);
        Assert.Contains("rule-based", transcript);
    }

    [Fact]
    public async Task Admin_ManagesChatbotKnowledge_AndTheAssistantUsesIt()
    {
        Assert.True(await DbAsync(db => db.AIKnowledgeEntries.CountAsync()) >= 6); // seeded at startup
        var admin = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

        var invalid = await PostFormAsync(admin, "/admin/ai-knowledge/create", "/admin/ai-knowledge/create", new Dictionary<string, string> { ["Title"] = "", ["Content"] = "", ["Category"] = "Hỏi đáp" });
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Vui lòng nhập tiêu đề.", await invalid.Content.ReadAsStringAsync());

        var created = await PostFormAsync(admin, "/admin/ai-knowledge/create", "/admin/ai-knowledge/create", new Dictionary<string, string>
        {
            ["Title"] = "Giờ làm việc xưởng",
            ["Content"] = "Xưởng nhận khách xem mẫu gỗ từ 8h đến 17h các ngày trong tuần.",
            ["Category"] = "Hỏi đáp",
            ["Keywords"] = "xưởng, xem mẫu gỗ",
            ["IsActive"] = "true",
            ["DisplayOrder"] = "10"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var id = await DbAsync(db => db.AIKnowledgeEntries.Where(k => k.Title == "Giờ làm việc xưởng").Select(k => k.Id).SingleAsync());

        var answer = await DataAsync(await PostJsonAsync(factory.CreateClient(), "/api/ai/chat", new { message = "Mình muốn qua xem mẫu gỗ ở xưởng được không?" }));
        Assert.Contains("8h đến 17h", answer.GetProperty("reply").GetString());

        var deleted = await PostFormAsync(admin, "/admin/ai-knowledge", $"/admin/ai-knowledge/{id}/delete", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.False(await DbAsync(db => db.AIKnowledgeEntries.AnyAsync(k => k.Id == id)));
        Assert.True(await DbAsync(db => db.AuditLogs.AnyAsync(l => l.EntityName == "AIKnowledgeEntry" && l.EntityId == id.ToString())));
    }

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMetaRegex();

    [GeneratedRegex("<form[^>]*method=\"post\"[^>]*>.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PostFormRegex();
}
