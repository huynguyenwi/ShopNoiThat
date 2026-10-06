using System.Text.Json;
using FurnitureStore.Application.AI;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Admin;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.AI;

/// <summary>
/// The assistant must only show real catalog products and prices - with the rule-based fallback and with a (scripted) AI model.
/// </summary>
public sealed class AssistantServiceTests : IAsyncLifetime
{
    private ServiceTestHost _host = null!;
    private static readonly AiCaller Guest = new(null, "0123456789abcdef0123456789abcdef");

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Assistant<T>(Func<IAssistantService, Task<T>> action) => _host.RunAsync(sp => action(sp.GetRequiredService<IAssistantService>()));
    private Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action) => _host.RunAsync(sp => action(sp.GetRequiredService<ApplicationDbContext>()));

    private Task<AiChatResponse> AskAsync(string message, AiCaller? caller = null, int? conversationId = null, int? productId = null) =>
        Assistant(a => a.ChatAsync(caller ?? Guest, new AiChatRequest { Message = message, ConversationId = conversationId, ProductId = productId }));

    private void ScriptAi(Func<AiCompletionRequest, string> content)
    {
        _host.Ai.IsConfigured = true;
        _host.Ai.Respond = request => new AiCompletionResult(content(request), "test-model", 120, 40);
    }

    /// <summary>Product ids listed in the SẢN PHẨM section of the system prompt.</summary>
    private static List<int> CandidateIds(AiCompletionRequest request)
    {
        var system = request.Messages[0].Content;
        var json = system[(system.IndexOf("SẢN PHẨM (JSON): ", StringComparison.Ordinal) + "SẢN PHẨM (JSON): ".Length)..];
        using var doc = JsonDocument.Parse(json[..(FindArrayEnd(json) + 1)]);
        return doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();
    }

    private static int FindArrayEnd(string json)
    {
        var depth = 0;
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (c == '"' && (i == 0 || json[i - 1] != '\\')) inString = !inString;
            if (inString) continue;
            if (c == '[') depth++;
            if (c == ']' && --depth == 0) return i;
        }

        return json.Length - 1;
    }

    // ------------------------------------------------------------------ rule-based assistant (no API key)

    [Fact]
    public async Task RuleBased_FindsRealProductsWithinBudget_AndSavesTheConversation()
    {
        var response = await AskAsync("Tôi cần một bộ bàn ăn cho gia đình 6 người, khoảng 10 triệu.");

        Assert.False(response.AiEnabled);
        Assert.NotEmpty(response.Products);
        Assert.Contains("bộ bàn ăn", response.Reply);
        Assert.Contains("loại sản phẩm", response.Reply); // tells the customer the suggestions are alternatives
        var ids = response.Products.Select(p => p.ProductId).ToList();
        var dbProducts = await Db(db => db.Products.Include(p => p.Category).Where(p => ids.Contains(p.Id)).ToListAsync());
        foreach (var card in response.Products)
        {
            var product = dbProducts.Single(p => p.Id == card.ProductId);
            Assert.Equal(product.DiscountPrice ?? product.BasePrice, card.Price); // price straight from the database
            Assert.Equal($"/products/{product.Slug}", card.Url);
            // Only one dining set exists and it costs 20M: the assistant offers a table / chairs bought separately instead.
            Assert.Contains(product.Category.Slug, new[] { "bo-ban-an", "ban-an", "ghe-an" });
            Assert.False(string.IsNullOrWhiteSpace(card.Reason));
        }

        var conversation = await Db(db => db.AIConversations.Include(c => c.Messages).SingleAsync(c => c.Id == response.ConversationId));
        Assert.Equal(Guest.AnonymousId, conversation.AnonymousId);
        Assert.Equal(2, conversation.Messages.Count);
        var answer = conversation.Messages.Single(m => m.Role == AIMessageRole.Assistant);
        Assert.Equal("rule-based", answer.Model);
        Assert.Contains(response.Products[0].ProductId.ToString(), answer.MetadataJson);
    }

    [Fact]
    public async Task RuleBased_UnderstandsFollowUps_InTheSameConversation()
    {
        var first = await AskAsync("Mình muốn mua bàn ăn cho 6 người");
        var second = await AskAsync("tầm 8 triệu thôi", conversationId: first.ConversationId);

        Assert.Equal(first.ConversationId, second.ConversationId);
        Assert.NotEmpty(second.Products);
        Assert.Contains("8 triệu", second.Reply);
        Assert.All(second.Products, p => Assert.True(p.Price <= 8_000_000m * 1.15m * 1.3m)); // at most one relaxation step
    }

    [Fact]
    public async Task RuleBased_AnswersPolicyQuestions_FromTheKnowledgeBase()
    {
        await Db(async db =>
        {
            db.AIKnowledgeEntries.Add(new AIKnowledgeEntry { Title = "Bảo hành", Content = "Bảo hành khung 24 tháng.", Category = "Chính sách", Keywords = "bảo hành, sửa chữa" });
            return await db.SaveChangesAsync();
        });

        var response = await AskAsync("Chính sách bảo hành thế nào vậy shop?");

        Assert.Contains("Bảo hành khung 24 tháng.", response.Reply);
        Assert.Empty(response.Products);
    }

    [Fact]
    public void KnowledgeKeywords_MatchWholeWords_AndPreferMoreHits()
    {
        AIKnowledgeEntry warranty = new() { Title = "Bảo hành", Keywords = "bảo hành, hỏng, lỗi", DisplayOrder = 1 };
        AIKnowledgeEntry workshop = new() { Title = "Xưởng", Keywords = "xưởng, xem mẫu gỗ", DisplayOrder = 2 };

        // "không" contains "hong" once accents are removed - it must not trigger "hỏng".
        Assert.Equal(["Xưởng"], AssistantService.SelectKnowledge([warranty, workshop], "Có xem mẫu gỗ ở xưởng không?").Select(k => k.Title));
        Assert.Equal(["Bảo hành"], AssistantService.SelectKnowledge([warranty, workshop], "ghe bi hong thi sao").Select(k => k.Title));
        Assert.Empty(AssistantService.SelectKnowledge([warranty, workshop], "xin chào"));
    }

    [Fact]
    public async Task RuleBased_ProductAdvice_DescribesTheProductWithDatabasePrices()
    {
        var product = await Db(db => db.Products.Include(p => p.Variants).FirstAsync(p => p.Sku == "SF-OSLO"));

        var response = await AskAsync("Tư vấn giúp mình sản phẩm này", productId: product.Id);

        Assert.Equal(product.Id, response.Products[0].ProductId);
        Assert.Contains(product.Name, response.Reply);
        Assert.Contains(AiPrompts.Vnd(product.Variants.Min(v => v.Price)), response.Reply);
        Assert.Equal(AIConversationType.ProductAdvice, await Db(db => db.AIConversations.Where(c => c.Id == response.ConversationId).Select(c => c.Type).SingleAsync()));
    }

    [Fact]
    public async Task RuleBased_NoMatch_SuggestsCustomOrder()
    {
        var response = await AskAsync("giường ngủ gỗ óc chó dưới 500k");

        Assert.Empty(response.Products);
        Assert.Contains("Báo giá", response.Reply);
    }

    // ------------------------------------------------------------------ with an AI model

    [Fact]
    public async Task Ai_InventedProductsAndPrices_AreRemovedServerSide()
    {
        int? realId = null;
        decimal realPrice = 0;
        ScriptAi(request =>
        {
            realId = CandidateIds(request)[0];
            realPrice = _host.RunAsync(sp => sp.GetRequiredService<ApplicationDbContext>().Products.Where(p => p.Id == realId).Select(p => p.DiscountPrice ?? p.BasePrice).SingleAsync()).Result;
            var realText = AiPrompts.Vnd(realPrice);
            return JsonSerializer.Serialize(new
            {
                reply = $"Mẫu đầu tiên giá {realText}. Mẫu khác chỉ 1.234.000đ, còn bàn siêu rẻ 3,7 triệu.",
                products = new object[] { new { id = 999_999, reason = "không tồn tại" }, new { id = realId, reason = "Vừa ngân sách" } },
                suggestions = new[] { "Có màu khác không?" }
            });
        });

        var response = await AskAsync("bàn ăn 6 người khoảng 15 triệu");

        Assert.True(response.AiEnabled);
        var card = Assert.Single(response.Products);             // the invented id 999999 is dropped
        Assert.Equal(realId, card.ProductId);
        Assert.Equal(realPrice, card.Price);
        Assert.Equal("Vừa ngân sách", card.Reason);
        Assert.Contains(AiPrompts.Vnd(realPrice), response.Reply); // a real catalog price is kept
        Assert.DoesNotContain("1.234.000đ", response.Reply);       // invented prices are replaced
        Assert.DoesNotContain("3,7 triệu", response.Reply);
        Assert.Contains(PriceGuard.Replacement, response.Reply);
        Assert.Equal(["Có màu khác không?"], response.Suggestions);

        var answer = await Db(db => db.AIMessages.SingleAsync(m => m.ConversationId == response.ConversationId && m.Role == AIMessageRole.Assistant));
        Assert.Equal("test-model", answer.Model);
        Assert.Equal(40, answer.CompletionTokens);
        Assert.Equal(160, await Db(db => db.AIConversations.Where(c => c.Id == response.ConversationId).Select(c => c.TotalTokens).SingleAsync()));
    }

    [Fact]
    public async Task Ai_PromptContainsRulesCatalogDataAndHistory_ButNoSecrets()
    {
        ScriptAi(_ => """{"reply":"Bạn cần bàn cho mấy người?","products":[],"suggestions":[]}""");
        var first = await AskAsync("Mình cần bàn ăn gỗ óc chó");
        await AskAsync("cho 6 người", conversationId: first.ConversationId);

        var request = _host.Ai.Requests[^1];
        var system = request.Messages[0];
        Assert.Equal(AIMessageRole.System, system.Role);
        Assert.Contains("Không tự đặt ra giá", system.Content);
        Assert.Contains("SẢN PHẨM (JSON)", system.Content);
        Assert.Contains("\"url\":\"/products/", system.Content);
        Assert.Equal(["Mình cần bàn ăn gỗ óc chó", "Bạn cần bàn cho mấy người?", "cho 6 người"],
            request.Messages.Skip(1).Select(m => m.Content));
        Assert.DoesNotContain("ApiKey", system.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ai_Failure_FallsBackToRuleBasedAnswer_AndIsLoggedAsError()
    {
        _host.Ai.IsConfigured = true;
        _host.Ai.Respond = _ => throw new AiUnavailableException("Dịch vụ AI đang quá tải.");

        var response = await AskAsync("sofa màu xám cho phòng khách");

        Assert.StartsWith("(Trợ lý AI đang bận", response.Reply);
        Assert.NotEmpty(response.Products);
        Assert.True(await Db(db => db.AIMessages.AnyAsync(m => m.ConversationId == response.ConversationId && m.IsError)));
    }

    [Fact]
    public async Task Ai_PlainTextAnswer_IsAccepted_AndInvalidJsonDoesNotCrash()
    {
        ScriptAi(_ => "Chào bạn, mình có thể giúp gì? (không phải JSON)");

        var response = await AskAsync("xin chào");

        Assert.Equal("Chào bạn, mình có thể giúp gì? (không phải JSON)", response.Reply);
        Assert.Empty(response.Products);
    }

    // ------------------------------------------------------------------ ownership & validation

    [Fact]
    public async Task Conversations_BelongToTheirOwner()
    {
        var mine = await AskAsync("bàn trà gỗ");
        var stranger = new AiCaller(null, "ffffffffffffffffffffffffffffffff");

        await Assert.ThrowsAsync<NotFoundException>(() => AskAsync("xem trộm", stranger, mine.ConversationId));
        await Assert.ThrowsAsync<NotFoundException>(() => Assistant(a => a.GetMyConversationAsync(stranger, mine.ConversationId)));
        Assert.Empty(await Assistant(a => a.GetMyConversationsAsync(stranger)));

        var history = await Assistant(a => a.GetMyConversationAsync(Guest, mine.ConversationId));
        Assert.Equal(2, history.Messages.Count);
        Assert.Equal(mine.Products.Select(p => p.ProductId), history.Messages[1].Products.Select(p => p.ProductId));
    }

    [Fact]
    public async Task GuestConversations_MoveToTheAccount_AfterSignIn()
    {
        var chat = await AskAsync("kệ sách");
        var userId = await AdminServiceTests.CreateCustomerAsync(_host);

        Assert.Equal(1, await Assistant(a => a.ClaimAnonymousConversationsAsync(Guest.AnonymousId!, userId)));

        var list = await Assistant(a => a.GetMyConversationsAsync(new AiCaller(userId, null)));
        Assert.Equal(chat.ConversationId, Assert.Single(list).Id);
        Assert.Empty(await Assistant(a => a.GetMyConversationsAsync(Guest)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrTooLongMessages_AreRejected(string message)
    {
        await Assert.ThrowsAsync<AppValidationException>(() => AskAsync(message));
        await Assert.ThrowsAsync<AppValidationException>(() => AskAsync(new string('a', AssistantService.MaxMessageLength + 1)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => AskAsync("xin chào", new AiCaller(null, null)));
    }

    // ------------------------------------------------------------------ structured advice

    [Fact]
    public async Task RoomRecommendation_ReturnsAFurnishedRoomWithinBudget()
    {
        var response = await Assistant(a => a.RecommendProductsAsync(Guest, new ProductRecommendationRequest
        {
            RoomType = "phòng khách", RoomAreaM2 = 20, Style = "hiện đại", Budget = 30_000_000
        }));

        Assert.True(response.Items.Count >= 2);
        var categories = await Db(db => db.Products.Where(p => response.Items.Select(i => i.ProductId).Contains(p.Id)).Select(p => p.Category.Slug).ToListAsync());
        Assert.Equal(categories.Count, categories.Distinct().Count()); // one piece per category (sofa, bàn trà, kệ tivi...)
        Assert.Contains("sofa", categories);
        Assert.Equal(response.Items.Sum(i => i.Price), response.TotalPrice);
        Assert.True(response.TotalPrice <= 30_000_000m * 1.05m);
        Assert.Equal(AIConversationType.Recommendation, await Db(db => db.AIConversations.Where(c => c.Id == response.ConversationId).Select(c => c.Type).SingleAsync()));
    }

    [Fact]
    public async Task Recommendation_WithoutAnyNeed_IsRejected()
    {
        await Assert.ThrowsAsync<AppValidationException>(() => Assistant(a => a.RecommendProductsAsync(Guest, new ProductRecommendationRequest())));
    }

    [Fact]
    public async Task ColorAdvice_UsesCatalogColors_AndOnlyTheRequestedFurniture()
    {
        var response = await Assistant(a => a.RecommendColorsAsync(Guest, new ColorRecommendationRequest { WallColor = "trắng", Furniture = "sofa" }));

        var advice = Assert.Single(response.Advice);
        Assert.Equal("Sofa", advice.Furniture);
        var catalogColors = await Db(db => db.ProductColors.Select(c => c.Slug).ToListAsync());
        Assert.All(advice.Colors, c => Assert.Contains(c.Slug, catalogColors));
        Assert.All(advice.Colors, c => Assert.StartsWith("#", c.Hex));
        Assert.Contains("Tường trắng", response.Summary);
        Assert.NotEmpty(response.Products);
        Assert.All(response.Products, p => Assert.Equal("Sofa", p.CategoryName));
    }

    [Fact]
    public async Task ColorAdvice_ByTheModel_IgnoresColorsThatAreNotSold()
    {
        ScriptAi(_ => """{"summary":"Tường xám hợp gỗ ấm.","advice":[{"furniture":"Bàn","colors":[{"slug":"tim-hoang-gia","reason":"màu không có"},{"slug":"nau-oc-cho","reason":"ấm"}]}],"productIds":[123456]}""");

        var response = await Assistant(a => a.RecommendColorsAsync(Guest, new ColorRecommendationRequest { WallColor = "xám", Furniture = "bàn ăn" }));

        var advice = Assert.Single(response.Advice);
        Assert.Equal("nau-oc-cho", Assert.Single(advice.Colors).Slug);
        Assert.Equal("Tường xám hợp gỗ ấm.", response.Summary);
        Assert.DoesNotContain(response.Products, p => p.ProductId == 123456);
    }

    [Fact]
    public async Task StyleAdvice_SmallRoomAndSmallBudget_PrefersAffordableCompactStyles()
    {
        var response = await Assistant(a => a.RecommendStylesAsync(Guest, new StyleRecommendationRequest
        {
            RoomAreaM2 = 12, Budget = 10_000_000, Preferences = "thích gọn gàng, đơn giản"
        }));

        Assert.Equal(3, response.Styles.Count);
        Assert.Contains(response.Styles[0].Slug, new[] { "toi-gian", "japandi", "bac-au" });
        Assert.DoesNotContain(response.Styles.Take(2), s => s.Slug is "sang-trong" or "co-dien");
        Assert.All(response.Products, p => Assert.True(p.Price <= 10_000_000m * 1.15m * 1.3m));
    }

    [Fact]
    public void StyleRanking_HonoursExplicitPreference()
    {
        var intent = new ShoppingIntent { RoomAreaM2 = 40, MaxBudget = 80_000_000 };
        intent.Styles.Add(("sang-trong", "Sang trọng"));

        var ranked = AssistantService.RankStyles(intent, "phong khach tiep khach");

        Assert.Equal("sang-trong", ranked[0].Profile.Slug);
        Assert.Contains("đúng phong cách bạn yêu thích", ranked[0].Reason, StringComparison.OrdinalIgnoreCase);
    }
}
