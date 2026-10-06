using System.Text.Json;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Application.AI;

/// <summary>Stored in <see cref="AIMessage.MetadataJson"/> for assistant answers.</summary>
public sealed record AiMessageMetadata(IReadOnlyList<int> ProductIds, IReadOnlyDictionary<string, string> Reasons, IReadOnlyList<string> Suggestions, bool UsedAi)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

public sealed class AssistantService(
    IAiChatClient aiClient,
    IAiConversationRepository conversations,
    IAiKnowledgeRepository knowledgeBase,
    ICatalogService catalog,
    ProductMatcher matcher,
    IProductFactRepository facts,
    IStoreInfoService storeInfo,
    Quotes.IQuoteService quoteService,
    Sales.ICouponRepository couponRepository,
    Sales.IOrderService orderService,
    IOptions<ShippingSettings> shippingOptions,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<AssistantService> logger) : IAssistantService
{
    public const int MaxMessageLength = 1000;
    private const int HistoryMessages = 8;

    private static readonly string[] DefaultSuggestions = ["Sofa cho phòng khách 20m²", "Bàn ăn 6 người khoảng 10 triệu", "Tường trắng nên chọn màu gì?"];

    /// <summary>The assistant's answer before it is saved and turned into product cards.</summary>
    private sealed record Answer(string Reply, IReadOnlyList<int> ProductIds, IReadOnlyDictionary<int, string> Reasons, IReadOnlyList<string> Suggestions,
        bool UsedAi, bool IsError = false, AiCompletionResult? Completion = null);

    public bool AiEnabled => aiClient.IsConfigured;

    // ================================================================== chatbot

    public async Task<AiChatResponse> ChatAsync(AiCaller caller, AiChatRequest request, CancellationToken cancellationToken = default)
    {
        RequireCaller(caller);
        var message = NormalizeMessage(request.Message);

        var conversation = request.ConversationId is int conversationId
            ? await conversations.GetOwnedAsync(conversationId, caller, cancellationToken) ?? throw new NotFoundException("cuộc trò chuyện", conversationId)
            : null;

        var focusId = request.ProductId ?? conversation?.ProductId;
        var focus = focusId is int id ? (await facts.GetFactsAsync([id], cancellationToken)).FirstOrDefault() : null;
        conversation ??= await StartConversationAsync(caller, focus is null ? AIConversationType.General : AIConversationType.ProductAdvice, message, focus?.Id, cancellationToken);

        // The intent accumulates over the last messages ("bàn ăn 6 người" ... "khoảng 10 triệu").
        var vocabulary = new CatalogVocabulary(await catalog.GetFilterOptionsAsync(cancellationToken));
        var earlier = await conversations.GetRecentUserMessagesAsync(conversation.Id, 3, cancellationToken);
        var intent = ShoppingIntentParser.Parse(string.Join(". ", earlier.Append(message)), vocabulary);
        var latest = ShoppingIntentParser.Parse(message, vocabulary);
        if (latest.Categories.Count > 0 && !latest.Categories.SequenceEqual(intent.Categories))
        {
            intent.Categories.Clear();
            intent.Categories.AddRange(latest.Categories); // the newest request wins ("còn ghế ăn thì sao?")
        }

        var match = intent.HasProductNeeds ? await matcher.FindAsync(intent, 6, cancellationToken) : ProductMatch.Empty;
        var candidates = match.Products.ToList();
        if (focus is not null && candidates.All(p => p.Id != focus.Id))
        {
            candidates.Insert(0, focus);
        }

        var store = await storeInfo.GetAsync(cancellationToken);
        var allKnowledge = await knowledgeBase.GetActiveAsync(cancellationToken);
        var knowledge = SelectKnowledge(allKnowledge, message);
        var local = LocalAssistant.Classify(message, focus is not null);

        await conversations.AddMessageAsync(NewMessage(conversation, AIMessageRole.User, message), cancellationToken);

        // Custom-order questions ("báo giá bàn óc chó 2m2...") are priced by the calculator, never by the model.
        if (IsCustomQuoteQuestion(message))
        {
            var quoteAnswer = await CustomQuoteAnswerAsync(message, cancellationToken);
            await SaveAnswerAsync(conversation, quoteAnswer.Answer, cancellationToken);
            return new AiChatResponse(conversation.Id, quoteAnswer.Answer.Reply, Cards(quoteAnswer.Products, quoteAnswer.Answer), quoteAnswer.Answer.Suggestions, aiClient.IsConfigured);
        }

        Answer answer;
        if (local is { Topic: LocalTopic.OrderStatus or LocalTopic.Coupons } && AnswerLocally(local, latest))
        {
            // The model has neither the customer's orders nor the running coupons (and must not invent codes):
            // these come straight from the database, AI or not.
            answer = await LocalAnswerAsync(caller, local, latest, match, candidates, focus, store, allKnowledge, cancellationToken);
        }
        else if (aiClient.IsConfigured)
        {
            try
            {
                var history = await conversations.GetRecentMessagesAsync(conversation.Id, HistoryMessages, cancellationToken);
                var system = AiPrompts.Chat(store, knowledge, intent, match.RelaxedConstraints, candidates, focus);
                var completion = await aiClient.CompleteAsync(new AiCompletionRequest(
                    [new AiChatMessage(AIMessageRole.System, system), .. history, new AiChatMessage(AIMessageRole.User, message)]), cancellationToken);
                answer = ReadChatAnswer(completion, candidates, AllowedAmounts(candidates, earlier.Append(message), knowledge));
            }
            catch (AiUnavailableException ex)
            {
                logger.LogWarning("AI chat failed for conversation {ConversationId}: {Reason}", conversation.Id, ex.Message);
                var fallback = await RuleBasedChatAsync(caller, message, local, intent, latest, match, candidates, focus, store, allKnowledge, knowledge, cancellationToken);
                answer = fallback with { Reply = "(Trợ lý AI đang bận, đây là gợi ý tự động.) " + fallback.Reply, IsError = true };
            }
        }
        else
        {
            answer = await RuleBasedChatAsync(caller, message, local, intent, latest, match, candidates, focus, store, allKnowledge, knowledge, cancellationToken);
        }

        await SaveAnswerAsync(conversation, answer, cancellationToken);
        return new AiChatResponse(conversation.Id, answer.Reply, Cards(candidates, answer), answer.Suggestions, aiClient.IsConfigured);
    }

    private Answer ReadChatAnswer(AiCompletionResult completion, IReadOnlyList<ProductFact> candidates, IReadOnlyCollection<decimal> allowedAmounts)
    {
        var reply = completion.Content;
        var ids = new List<int>();
        var reasons = new Dictionary<int, string>();
        var suggestions = new List<string>();

        if (ModelJson.TryParse(completion.Content, out var root))
        {
            reply = ModelJson.String(root, "reply") ?? string.Empty;
            foreach (var item in ModelJson.Array(root, "products"))
            {
                if (ModelJson.Int(item, "id") is int id && candidates.Any(c => c.Id == id) && !ids.Contains(id) && ids.Count < 4)
                {
                    ids.Add(id);
                    if (ModelJson.String(item, "reason") is { Length: > 0 } reason) reasons[id] = Clip(reason, 300);
                }
            }

            suggestions.AddRange(ModelJson.Array(root, "suggestions").Where(s => s.ValueKind == JsonValueKind.String)
                .Select(s => Clip(s.GetString()!, 60)).Where(s => s.Length > 0).Take(3));
        }

        if (string.IsNullOrWhiteSpace(reply))
        {
            throw new AiUnavailableException("Trợ lý AI trả về nội dung không hợp lệ.");
        }

        reply = PriceGuard.Sanitize(Clip(reply.Trim(), 2000), allowedAmounts, out var replaced);
        if (replaced > 0)
        {
            logger.LogWarning("Removed {Count} price(s) not found in the catalog from an AI answer", replaced);
        }

        return new Answer(reply, ids, reasons, suggestions.Count > 0 ? suggestions : DefaultSuggestions, UsedAi: true, Completion: completion);
    }

    /// <summary>
    /// Answers without an AI model: built-in answers for everyday questions (<see cref="LocalAssistant"/>), then the
    /// admin's own FAQ entries, then product advice / search from the catalog.
    /// </summary>
    private async Task<Answer> RuleBasedChatAsync(AiCaller caller, string message, LocalMatch? local, ShoppingIntent intent, ShoppingIntent latest, ProductMatch match,
        List<ProductFact> candidates, ProductFact? focus, StoreInfoDto store, IReadOnlyList<AIKnowledgeEntry> allKnowledge,
        IReadOnlyList<AIKnowledgeEntry> knowledge, CancellationToken cancellationToken)
    {
        if (AnswerLocally(local, latest))
        {
            return await LocalAnswerAsync(caller, local!, latest, match, candidates, focus, store, allKnowledge, cancellationToken);
        }

        // FAQ entries added by admins at /admin/ai-knowledge that the built-in topics do not cover.
        if (!latest.HasProductNeeds && knowledge.Count > 0 && focus is null)
        {
            var entry = knowledge[0];
            return new Answer($"{entry.Title}: {entry.Content}", [], new Dictionary<int, string>(),
                ["Còn chính sách nào khác?", "Gợi ý sản phẩm cho mình", "Chat với nhân viên"], UsedAi: false);
        }

        if (focus is not null && !intent.HasProductNeeds)
        {
            var sizes = focus.Sizes.Count > 0 ? string.Join(", ", focus.Sizes.Select(s => s.Name)) : "tiêu chuẩn";
            var price = focus.MaxPrice > focus.Price
                ? $"giá từ {AiPrompts.Vnd(focus.Price)} đến {AiPrompts.Vnd(focus.MaxPrice)} tùy phiên bản"
                : $"giá {AiPrompts.Vnd(focus.Price)}";
            var reply = $"{focus.Name} thuộc nhóm {focus.CategoryName.ToLowerInvariant()}"
                        + (focus.StyleName is null ? "" : $", phong cách {focus.StyleName.ToLowerInvariant()}")
                        + $". Chất liệu: {string.Join(", ", focus.Materials)}; màu: {string.Join(", ", focus.Colors)}; kích thước: {sizes}; {price}"
                        + (focus.InStock ? ", hiện còn hàng." : ", hiện tạm hết hàng.")
                        + (focus.ReviewCount > 0 ? $" Khách đánh giá {focus.AverageRating:0.0}/5 ({focus.ReviewCount} lượt)." : "")
                        + " Bạn muốn mình so sánh với mẫu khác hay tư vấn màu phù hợp phòng của bạn?";
            var alternatives = candidates.Where(c => c.Id != focus.Id).Take(3).Select(c => c.Id).ToList();
            return new Answer(reply, [focus.Id, .. alternatives], new Dictionary<int, string> { [focus.Id] = "Sản phẩm bạn đang xem" },
                ["Có màu nào khác không?", "Mẫu nào rẻ hơn?", "Giao hàng mất bao lâu?"], UsedAi: false);
        }

        // Nothing to look for: no need so far, or a new message that neither names one nor follows up on the last search
        // ("xyz", "thời tiết hôm nay?") - say so instead of repeating the previous results.
        if (!intent.HasProductNeeds || !latest.HasProductNeeds && !LocalAssistant.IsFollowUp(message))
        {
            var unknown = LocalAssistant.Unknown(store);
            return new Answer(unknown.Text, [], new Dictionary<int, string>(), unknown.Suggestions, UsedAi: false);
        }

        if (match.Products.Count == 0)
        {
            return new Answer(
                $"Hiện cửa hàng chưa có mẫu phù hợp với {intent.Describe()}. Xưởng của Nhà Mộc nhận đóng theo kích thước và chất liệu bạn muốn - "
                + "bạn có thể nhận báo giá dự kiến ở trang Báo giá (/bao-gia) hoặc chat với nhân viên để được tư vấn thêm.",
                [], new Dictionary<int, string>(), ["Tìm mẫu tương tự rẻ hơn", "Đặt đóng theo yêu cầu", "Chat với nhân viên"], UsedAi: false);
        }

        var text = $"Mình tìm được {match.Products.Count} mẫu phù hợp với {intent.Describe()}.";
        if (match.RelaxedConstraints.Count > 0)
        {
            text += $" Một số mẫu chưa khớp hoàn toàn về {string.Join(", ", match.RelaxedConstraints)} - đó là lựa chọn gần nhất hiện có.";
        }

        var questions = new List<string>();
        if (intent.MaxBudget is null && intent.MinBudget is null) questions.Add("ngân sách khoảng bao nhiêu");
        if (intent.LengthMm is null && intent.People is null && intent.RoomAreaM2 is null) questions.Add("kích thước hoặc diện tích phòng");
        if (questions.Count > 0)
        {
            text += $" Bạn cho mình biết thêm {string.Join(" và ", questions)} để lọc chính xác hơn nhé.";
        }

        var ids = match.Products.Take(4).Select(p => p.Id).ToList();
        var reasons = match.Products.Take(4).ToDictionary(p => p.Id, p => ReasonFor(p, intent));
        return new Answer(text, ids, reasons, ["Có mẫu rẻ hơn không?", "Tư vấn màu phù hợp", "Chat với nhân viên"], UsedAi: false);
    }

    /// <summary>
    /// Whether the built-in answer fits. <paramref name="latest"/> is the newest message alone: "cảm ơn" after a product
    /// search is small talk, not the search again. A question naming products needs a clear topic ("sofa da thật có bền
    /// không?"), and concrete shopping requests with a budget / size / party size stay product searches.
    /// </summary>
    private static bool AnswerLocally(LocalMatch? local, ShoppingIntent latest)
    {
        if (local is null) return false;
        if (!latest.HasProductNeeds) return true;
        if (local.Score < LocalAssistant.StrongScore) return false;

        var concrete = latest.MaxBudget.HasValue || latest.MinBudget.HasValue || latest.People.HasValue || latest.LengthMm.HasValue || latest.RoomAreaM2.HasValue;
        return !(concrete && local.Topic is LocalTopic.Coupons or LocalTopic.HowToOrder or LocalTopic.Greeting or LocalTopic.Thanks
            or LocalTopic.Goodbye or LocalTopic.Help or LocalTopic.Identity);
    }

    private static bool IsProductPageTopic(LocalTopic topic) =>
        topic is LocalTopic.FocusPrice or LocalTopic.FocusSize or LocalTopic.FocusColor or LocalTopic.FocusMaterial or LocalTopic.FocusStock;

    /// <summary>Loads only the data the topic needs, builds the built-in answer and the product cards shown with it.</summary>
    private async Task<Answer> LocalAnswerAsync(AiCaller caller, LocalMatch local, ShoppingIntent latest, ProductMatch match, List<ProductFact> candidates,
        ProductFact? focus, StoreInfoDto store, IReadOnlyList<AIKnowledgeEntry> allKnowledge, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyList<Coupon> coupons = local.Topic == LocalTopic.Coupons
            ? (await couponRepository.ListOfferableAsync(now, cancellationToken)).OrderBy(c => c.MinOrderAmount).ThenBy(c => c.Code, StringComparer.Ordinal).ToList()
            : [];
        IReadOnlyList<Sales.OrderListItemDto>? myOrders = local.Topic == LocalTopic.OrderStatus && caller.UserId is string userId
            ? (await orderService.GetMyOrdersAsync(userId, 1, 3, cancellationToken)).Items
            : null;
        int? warrantyMonths = local.Topic == LocalTopic.Warranty && focus is not null
            ? (await catalog.GetProductByIdAsync(focus.Id, cancellationToken))?.WarrantyMonths
            : null;

        IReadOnlyList<ProductFact> cheaper = [];
        if (local.Topic == LocalTopic.FocusCheaper && focus is not null)
        {
            var page = await catalog.GetProductsAsync(new ProductQuery
            {
                CategorySlug = focus.CategorySlug, MaxPrice = focus.Price - 1, Sort = ProductSort.PriceDesc, PageSize = 4
            }, cancellationToken);
            var ids = page.Items.Select(i => i.Id).Where(id => id != focus.Id).ToList();
            cheaper = ids.Count == 0 ? [] : (await facts.GetFactsAsync(ids, cancellationToken)).OrderByDescending(p => p.Price).ToList();
        }

        var reply = LocalAssistant.Answer(local, new LocalFacts(store, shippingOptions.Value, allKnowledge, coupons, myOrders, focus, warrantyMonths, cheaper));

        // Cards: cheaper alternatives, the product being asked about, or products for needs named in the same question.
        List<ProductFact> shown = local.Topic == LocalTopic.FocusCheaper ? cheaper.ToList()
            : focus is not null && (IsProductPageTopic(local.Topic) || local.Topic is LocalTopic.Warranty or LocalTopic.Shipping) ? [focus]
            : latest.HasProductNeeds ? match.Products.Take(4).ToList()
            : [];
        foreach (var product in shown.Where(p => candidates.All(c => c.Id != p.Id)))
        {
            candidates.Add(product);
        }

        var reasons = shown.ToDictionary(p => p.Id, p =>
            p.Id == focus?.Id ? "Sản phẩm bạn đang xem"
            : local.Topic == LocalTopic.FocusCheaper ? $"Rẻ hơn, cùng nhóm {p.CategoryName.ToLowerInvariant()}"
            : ReasonFor(p, latest));
        return new Answer(reply.Text, shown.Select(p => p.Id).ToList(), reasons, reply.Suggestions, UsedAi: false);
    }

    // ================================================================== product recommendation

    public async Task<ProductRecommendationResponse> RecommendProductsAsync(AiCaller caller, ProductRecommendationRequest request, CancellationToken cancellationToken = default)
    {
        RequireCaller(caller);
        var description = DescribeRequest(request);
        if (description.Length == 0)
        {
            throw new AppValidationException("Needs", "Vui lòng cho biết ít nhất một nhu cầu (phòng, loại sản phẩm, ngân sách...).");
        }

        var vocabulary = new CatalogVocabulary(await catalog.GetFilterOptionsAsync(cancellationToken));
        var intent = ShoppingIntentParser.Parse(description, vocabulary);
        if (request.Budget is > 0) { intent.MaxBudget = request.Budget; intent.MinBudget = null; intent.BudgetIsApproximate = false; }
        if (request.People is > 0) intent.People = request.People;
        if (request.RoomAreaM2 is > 0) intent.RoomAreaM2 = request.RoomAreaM2;

        var roomSet = intent.RoomSlug is not null && intent.Categories.Count == 0;
        var match = roomSet ? await matcher.FindRoomSetAsync(intent, cancellationToken) : await matcher.FindAsync(intent, 6, cancellationToken);

        var conversation = await StartConversationAsync(caller, AIConversationType.Recommendation, "Gợi ý: " + description, null, cancellationToken);
        await conversations.AddMessageAsync(NewMessage(conversation, AIMessageRole.User, description), cancellationToken);

        Answer answer;
        var fallback = RuleBasedRecommendation(intent, match, roomSet);
        if (aiClient.IsConfigured && match.Products.Count > 0)
        {
            try
            {
                var store = await storeInfo.GetAsync(cancellationToken);
                var completion = await aiClient.CompleteAsync(new AiCompletionRequest(
                [
                    new AiChatMessage(AIMessageRole.System, AiPrompts.Recommend(store, description, intent, match.RelaxedConstraints, match.Products, roomSet)),
                    new AiChatMessage(AIMessageRole.User, description)
                ]), cancellationToken);
                answer = ReadListAnswer(completion, "summary", "items", match.Products, AllowedAmounts(match.Products, [description], []), fallback, keepAll: roomSet);
            }
            catch (AiUnavailableException ex)
            {
                logger.LogWarning("AI recommendation failed: {Reason}", ex.Message);
                answer = fallback with { IsError = true };
            }
        }
        else
        {
            answer = fallback;
        }

        await SaveAnswerAsync(conversation, answer, cancellationToken);
        var cards = Cards(match.Products, answer);
        return new ProductRecommendationResponse(answer.Reply, cards, roomSet && cards.Count > 0 ? cards.Sum(c => c.Price) : null, aiClient.IsConfigured, conversation.Id);
    }

    private static Answer RuleBasedRecommendation(ShoppingIntent intent, ProductMatch match, bool roomSet)
    {
        if (match.Products.Count == 0)
        {
            return new Answer($"Chưa có sản phẩm có sẵn phù hợp với {intent.Describe()}. Bạn có thể đặt đóng theo kích thước riêng ở trang Báo giá (/bao-gia).",
                [], new Dictionary<int, string>(), [], UsedAi: false);
        }

        var total = match.Products.Sum(p => p.Price);
        var summary = roomSet
            ? $"Gợi ý một bộ {match.Products.Count} món cho {intent.RoomName?.ToLowerInvariant() ?? "căn phòng"} với tổng giá {AiPrompts.Vnd(total)}"
              + (intent.MaxBudget is decimal budget ? (total <= budget ? $", nằm trong ngân sách {ShoppingIntent.Money(budget)}." : $", nhỉnh hơn ngân sách {ShoppingIntent.Money(budget)} một chút.") : ".")
            : $"Các mẫu phù hợp nhất với {intent.Describe()}.";
        if (match.RelaxedConstraints.Count > 0)
        {
            summary += $" Một số mẫu chưa khớp hoàn toàn về {string.Join(", ", match.RelaxedConstraints)} - đó là lựa chọn gần nhất hiện có.";
        }

        if (intent.Styles.Count > 0 && AdviceKnowledge.Styles.FirstOrDefault(s => s.Slug == intent.Styles[0].Slug) is { } style)
        {
            summary += $" Phong cách {style.Name.ToLowerInvariant()}: {style.KeyPoints[0].ToLowerInvariant()}.";
        }

        return new Answer(summary, match.Products.Select(p => p.Id).ToList(), match.Products.ToDictionary(p => p.Id, p => ReasonFor(p, intent)), [], UsedAi: false);
    }

    // ================================================================== colors

    public async Task<ColorRecommendationResponse> RecommendColorsAsync(AiCaller caller, ColorRecommendationRequest request, CancellationToken cancellationToken = default)
    {
        RequireCaller(caller);
        var description = DescribeRequest(request);
        if (description.Length == 0)
        {
            throw new AppValidationException("WallColor", "Vui lòng cho biết màu tường hoặc món nội thất bạn muốn phối màu.");
        }

        var options = await catalog.GetFilterOptionsAsync(cancellationToken);
        var vocabulary = new CatalogVocabulary(options);
        var palette = options.Colors.ToDictionary(c => c.Slug, c => (c.Name, Hex: c.Hex ?? "#cccccc"));

        var wall = ShoppingIntentParser.Parse(request.WallColor, vocabulary);
        var floorPlain = ShoppingIntentParser.Plain(request.FloorColor ?? string.Empty);
        var tone = AdviceKnowledge.ToneOf(wall.Colors.Select(c => c.Slug).ToList(), ShoppingIntentParser.Plain(request.WallColor ?? string.Empty));
        var whole = ShoppingIntentParser.Parse(description, vocabulary);
        var style = whole.Styles.Select(s => AdviceKnowledge.Styles.FirstOrDefault(p => p.Slug == s.Slug)).FirstOrDefault(p => p is not null);

        // Baseline advice from color-harmony rules, limited to the pieces the customer asked about.
        var wanted = WantedFurniture(ShoppingIntentParser.Plain($"{request.Furniture} {request.Note}"));
        var baseline = AdviceKnowledge.PaletteFor(tone).Where(p => wanted.Count == 0 || wanted.Contains(p.Furniture)).ToList();
        if (style is not null)
        {
            baseline = baseline.Select(p => p with
            {
                Colors = p.Colors.OrderByDescending(c => style.ColorSlugs.Contains(c.Slug)).ToList()
            }).ToList();
        }

        var darkFloor = floorPlain.Contains("toi") || floorPlain.Contains("dam") || floorPlain.Contains("oc cho") || floorPlain.Contains("nau");
        var products = await ProductsForPaletteAsync(baseline, cancellationToken);

        var conversation = await StartConversationAsync(caller, AIConversationType.ColorAdvice, "Tư vấn màu: " + description, null, cancellationToken);
        await conversations.AddMessageAsync(NewMessage(conversation, AIMessageRole.User, description), cancellationToken);

        var advice = baseline.Select(p => new FurnitureColorAdviceDto(p.Furniture,
            p.Colors.Where(c => palette.ContainsKey(c.Slug)).Take(3)
                .Select(c => new ColorSuggestionDto(palette[c.Slug].Name, c.Slug, palette[c.Slug].Hex, c.Reason)).ToList())).ToList();
        var summary = ToneSummary(tone, darkFloor, style);
        Answer answer = new(summary, products.Take(6).Select(p => p.Id).ToList(), new Dictionary<int, string>(), [], UsedAi: false);

        if (aiClient.IsConfigured)
        {
            try
            {
                var store = await storeInfo.GetAsync(cancellationToken);
                var completion = await aiClient.CompleteAsync(new AiCompletionRequest(
                [
                    new AiChatMessage(AIMessageRole.System, AiPrompts.Colors(store, description, palette.Select(p => (p.Key, p.Value.Name, p.Value.Hex)).ToList(), baseline, products)),
                    new AiChatMessage(AIMessageRole.User, description)
                ]), cancellationToken);

                if (ModelJson.TryParse(completion.Content, out var root))
                {
                    var modelAdvice = ModelJson.Array(root, "advice").Select(item =>
                    {
                        var furniture = Clip(ModelJson.String(item, "furniture") ?? string.Empty, 40);
                        var colors = ModelJson.Array(item, "colors")
                            .Select(c => (Slug: ModelJson.String(c, "slug") ?? string.Empty, Reason: ModelJson.String(c, "reason") ?? string.Empty))
                            .Where(c => palette.ContainsKey(c.Slug)) // only colors that exist in the catalog
                            .Take(3)
                            .Select(c => new ColorSuggestionDto(palette[c.Slug].Name, c.Slug, palette[c.Slug].Hex, Clip(c.Reason, 200)))
                            .ToList();
                        return new FurnitureColorAdviceDto(furniture, colors);
                    }).Where(a => a.Furniture.Length > 0 && a.Colors.Count > 0).ToList();

                    if (modelAdvice.Count > 0) advice = modelAdvice;
                    var modelSummary = ModelJson.String(root, "summary");
                    var ids = ModelJson.Array(root, "productIds").Select(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) ? v : 0)
                        .Where(id => products.Any(p => p.Id == id)).Distinct().Take(6).ToList();
                    answer = new Answer(
                        string.IsNullOrWhiteSpace(modelSummary) ? summary : PriceGuard.Sanitize(Clip(modelSummary, 1000), AllowedAmounts(products, [description], []), out _),
                        ids.Count > 0 ? ids : answer.ProductIds, new Dictionary<int, string>(), [], UsedAi: true, Completion: completion);
                }
                else
                {
                    answer = answer with { UsedAi = true, Completion = completion };
                }
            }
            catch (AiUnavailableException ex)
            {
                logger.LogWarning("AI color advice failed: {Reason}", ex.Message);
                answer = answer with { IsError = true };
            }
        }

        var stored = answer with
        {
            Reply = answer.Reply + "\n" + string.Join("\n", advice.Select(a => $"- {a.Furniture}: {string.Join(", ", a.Colors.Select(c => $"{c.Name} ({c.Reason})"))}"))
        };
        await SaveAnswerAsync(conversation, stored, cancellationToken);
        return new ColorRecommendationResponse(answer.Reply, advice, Cards(products, answer), aiClient.IsConfigured, conversation.Id);
    }

    private async Task<IReadOnlyList<ProductFact>> ProductsForPaletteAsync(IReadOnlyList<AdviceKnowledge.PaletteEntry> baseline, CancellationToken cancellationToken)
    {
        var ids = new List<int>();
        foreach (var entry in baseline)
        {
            if (!AdviceKnowledge.FurnitureCategories.TryGetValue(entry.Furniture, out var categories)) continue;
            foreach (var category in categories.Take(2))
            {
                var page = await catalog.GetProductsAsync(new ProductQuery
                {
                    CategorySlug = category,
                    ColorSlugs = entry.Colors.Select(c => c.Slug).ToList(),
                    InStock = true,
                    Sort = ProductSort.BestSelling,
                    PageSize = 2
                }, cancellationToken);
                ids.AddRange(page.Items.Select(p => p.Id).Where(id => !ids.Contains(id)).Take(1));
            }
        }

        return await facts.GetFactsAsync(ids.Take(8).ToList(), cancellationToken);
    }

    private static List<string> WantedFurniture(string plain)
    {
        var wanted = new List<string>();
        foreach (var (furniture, words) in AdviceKnowledge.FurnitureWords)
        {
            if (words.Any(w => System.Text.RegularExpressions.Regex.IsMatch(plain, $@"(?<![\p{{L}}]){System.Text.RegularExpressions.Regex.Escape(w)}(?![\p{{L}}])")))
            {
                wanted.Add(furniture);
            }
        }

        return wanted;
    }

    private static string ToneSummary(AdviceKnowledge.WallTone tone, bool darkFloor, AdviceKnowledge.StyleProfile? style)
    {
        var text = tone switch
        {
            AdviceKnowledge.WallTone.Gray => "Tường xám là nền trung tính hơi lạnh: nên thêm gỗ ấm (óc chó, sồi) và vải màu kem hoặc một màu nhấn đậm như xanh navy, cognac.",
            AdviceKnowledge.WallTone.Dark => "Tường tối dễ làm phòng bí: ưu tiên nội thất sáng màu (gỗ sồi, kem, mặt đá trắng) để tạo tương phản và phản chiếu ánh sáng.",
            AdviceKnowledge.WallTone.Colorful => "Tường đã có màu thì để tường làm điểm nhấn: nội thất nên trung tính (trắng, kem, gỗ sáng) để không bị rối mắt.",
            AdviceKnowledge.WallTone.Wood => "Không gian nhiều gỗ nên tạo chiều sâu bằng tông gỗ khác biệt và thêm vải kem, xanh rêu hoặc mặt đá để bớt đơn điệu.",
            _ => "Tường trắng/sáng rất dễ phối: gỗ sáng giữ phòng thoáng, còn gỗ óc chó hoặc sofa màu đậm sẽ tạo điểm nhấn ấm áp. Áp dụng quy tắc 60-30-10: 60% màu nền, 30% màu nội thất chính, 10% màu nhấn."
        };
        if (darkFloor) text += " Sàn màu tối nên chọn đồ nội thất sáng hơn sàn một tông để không gian không bị nặng.";
        if (style is not null) text += $" Với phong cách {style.Name.ToLowerInvariant()}, ưu tiên: {string.Join(", ", style.KeyPoints).ToLowerInvariant()}.";
        return text;
    }

    // ================================================================== styles

    public async Task<StyleRecommendationResponse> RecommendStylesAsync(AiCaller caller, StyleRecommendationRequest request, CancellationToken cancellationToken = default)
    {
        RequireCaller(caller);
        var description = DescribeRequest(request);
        if (description.Length == 0)
        {
            throw new AppValidationException("Preferences", "Vui lòng cho biết diện tích, ngân sách hoặc sở thích của bạn.");
        }

        var options = await catalog.GetFilterOptionsAsync(cancellationToken);
        var vocabulary = new CatalogVocabulary(options);
        var intent = ShoppingIntentParser.Parse(description, vocabulary);
        if (request.Budget is > 0) { intent.MaxBudget = request.Budget; intent.BudgetIsApproximate = true; }
        if (request.RoomAreaM2 is > 0) intent.RoomAreaM2 = request.RoomAreaM2;

        var ranked = RankStyles(intent, ShoppingIntentParser.Plain(description))
            .Where(r => options.Styles.Any(s => s.Slug == r.Profile.Slug))
            .Take(3).ToList();

        var productIntent = new ShoppingIntent { MaxBudget = intent.MaxBudget, BudgetIsApproximate = true, RoomSlug = intent.RoomSlug, RoomName = intent.RoomName, RoomAreaM2 = intent.RoomAreaM2 };
        productIntent.Categories.AddRange(intent.Categories);
        productIntent.Styles.Add((ranked[0].Profile.Slug, ranked[0].Profile.Name));
        var match = productIntent.RoomSlug is not null && productIntent.Categories.Count == 0
            ? await matcher.FindRoomSetAsync(productIntent, cancellationToken)
            : await matcher.FindAsync(productIntent, 6, cancellationToken);

        var conversation = await StartConversationAsync(caller, AIConversationType.StyleAdvice, "Chọn phong cách: " + description, null, cancellationToken);
        await conversations.AddMessageAsync(NewMessage(conversation, AIMessageRole.User, description), cancellationToken);

        var styles = ranked.Select(r => new StyleSuggestionDto(r.Profile.Name, r.Profile.Slug, r.Profile.Description, r.Reason, r.Profile.KeyPoints)).ToList();
        var summary = $"Phong cách hợp nhất với bạn là {ranked[0].Profile.Name}: {ranked[0].Reason.ToLowerInvariant()}"
                      + (ranked.Count > 1 ? $" Bạn cũng có thể cân nhắc {string.Join(" hoặc ", ranked.Skip(1).Select(r => r.Profile.Name))}." : "");
        Answer answer = new(summary, match.Products.Select(p => p.Id).ToList(), match.Products.ToDictionary(p => p.Id, p => ReasonFor(p, productIntent)), [], UsedAi: false);

        if (aiClient.IsConfigured)
        {
            try
            {
                var store = await storeInfo.GetAsync(cancellationToken);
                var completion = await aiClient.CompleteAsync(new AiCompletionRequest(
                [
                    new AiChatMessage(AIMessageRole.System, AiPrompts.Styles(store, description, AdviceKnowledge.Styles, ranked.Select(r => r.Profile.Slug).ToList(), match.Products)),
                    new AiChatMessage(AIMessageRole.User, description)
                ]), cancellationToken);

                if (ModelJson.TryParse(completion.Content, out var root))
                {
                    var modelStyles = ModelJson.Array(root, "styles")
                        .Select(s => (Slug: ModelJson.String(s, "slug") ?? string.Empty, Reason: ModelJson.String(s, "reason") ?? string.Empty))
                        .Select(s => (Profile: AdviceKnowledge.Styles.FirstOrDefault(p => p.Slug == s.Slug), s.Reason))
                        .Where(s => s.Profile is not null && options.Styles.Any(o => o.Slug == s.Profile.Slug))
                        .DistinctBy(s => s.Profile!.Slug).Take(3)
                        .Select(s => new StyleSuggestionDto(s.Profile!.Name, s.Profile.Slug, s.Profile.Description, Clip(s.Reason, 300), s.Profile.KeyPoints))
                        .ToList();
                    if (modelStyles.Count > 0) styles = modelStyles;

                    var ids = ModelJson.Array(root, "productIds").Select(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) ? v : 0)
                        .Where(id => match.Products.Any(p => p.Id == id)).Distinct().Take(6).ToList();
                    var modelSummary = ModelJson.String(root, "summary");
                    answer = answer with
                    {
                        Reply = string.IsNullOrWhiteSpace(modelSummary) ? summary : PriceGuard.Sanitize(Clip(modelSummary, 1000), AllowedAmounts(match.Products, [description], []), out _),
                        ProductIds = ids.Count > 0 ? ids : answer.ProductIds,
                        UsedAi = true,
                        Completion = completion
                    };
                }
            }
            catch (AiUnavailableException ex)
            {
                logger.LogWarning("AI style advice failed: {Reason}", ex.Message);
                answer = answer with { IsError = true };
            }
        }

        await SaveAnswerAsync(conversation, answer with { Reply = answer.Reply + "\n" + string.Join("\n", styles.Select(s => $"- {s.Name}: {s.Reason}")) }, cancellationToken);
        return new StyleRecommendationResponse(answer.Reply, styles, Cards(match.Products, answer), aiClient.IsConfigured, conversation.Id);
    }

    /// <summary>Scores the 8 styles against area, budget, colors, purpose and explicit preferences. Public for tests.</summary>
    public static IReadOnlyList<(AdviceKnowledge.StyleProfile Profile, string Reason)> RankStyles(ShoppingIntent intent, string plain)
    {
        var results = new List<(AdviceKnowledge.StyleProfile Profile, double Score, List<string> Why)>();
        foreach (var style in AdviceKnowledge.Styles)
        {
            double score = 0;
            var why = new List<string>();
            if (intent.Styles.Any(s => s.Slug == style.Slug)) { score += 5; why.Add("đúng phong cách bạn yêu thích"); }

            if (intent.RoomAreaM2 is decimal area)
            {
                if (area < 15 && style.GoodFor.Contains("phòng nhỏ")) { score += 2; why.Add($"giúp phòng {area:0.#}m² trông rộng và gọn"); }
                if (area > 30 && style.GoodFor.Contains("không gian rộng")) { score += 2; why.Add($"phát huy không gian rộng {area:0.#}m²"); }
            }

            if (intent.MaxBudget is decimal budget)
            {
                var level = budget < 15_000_000 ? 1 : budget <= 50_000_000 ? 2 : 3;
                if (style.BudgetLevel == level) { score += 1.5; why.Add("phù hợp mức ngân sách"); }
                else if (style.BudgetLevel > level) score -= 1.5 * (style.BudgetLevel - level);
            }

            var colorHits = intent.Colors.Count(c => style.ColorSlugs.Contains(c.Slug));
            if (colorHits > 0) { score += 1.5 * colorHits; why.Add("bảng màu trùng với màu bạn thích"); }
            var materialHits = intent.Materials.Count(m => style.MaterialSlugs.Contains(m.Slug));
            if (materialHits > 0) { score += materialHits; why.Add("dùng chất liệu bạn thích"); }

            foreach (var goodFor in style.GoodFor)
            {
                var key = ShoppingIntentParser.Plain(goodFor);
                if (key.Split(' ').Where(w => w.Length > 2).Count(w => plain.Contains(w)) >= Math.Min(2, key.Split(' ').Length))
                {
                    score += 1.5;
                    why.Add($"hợp với nhu cầu \"{goodFor}\"");
                }
            }

            if (plain.Contains("tre nho") || plain.Contains("con nho")) { if (style.Slug is "bac-au" or "hien-dai") { score += 1; why.Add("an toàn, dễ vệ sinh cho gia đình có trẻ nhỏ"); } }
            if (plain.Contains("thien nhien") || plain.Contains("cay xanh")) { if (style.Slug is "japandi" or "moc-mac") { score += 1.5; why.Add("gần gũi thiên nhiên"); } }
            if (plain.Contains("sang") || plain.Contains("tiep khach")) { if (style.Slug is "sang-trong" or "co-dien") { score += 1; why.Add("tạo ấn tượng khi tiếp khách"); } }
            if (plain.Contains("ca tinh") || plain.Contains("kim loai") || plain.Contains("sat")) { if (style.Slug == "cong-nghiep") { score += 1.5; why.Add("cá tính với gỗ và kim loại"); } }
            if (plain.Contains("gon") || plain.Contains("don gian") || plain.Contains("it do")) { if (style.Slug is "toi-gian" or "japandi") { score += 1.5; why.Add("gọn gàng, ít chi tiết"); } }

            results.Add((style, score, why));
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Profile.BudgetLevel)
            .Select(r => (r.Profile, r.Why.Count > 0 ? Capitalize(string.Join(", ", r.Why.Distinct())) + "." : r.Profile.Description))
            .ToList();
    }

    // ================================================================== price estimate (custom furniture)

    private static bool IsCustomQuoteQuestion(string message)
    {
        var plain = ShoppingIntentParser.Plain(message);
        return plain.Contains("bao gia") || plain.Contains("dat dong") || plain.Contains("dong theo") || plain.Contains("lam theo kich thuoc")
               || plain.Contains("theo yeu cau") || plain.Contains("kich thuoc rieng") || plain.Contains("custom");
    }

    private async Task<(Answer Answer, IReadOnlyList<ProductFact> Products)> CustomQuoteAnswerAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            var estimate = await quoteService.EstimateAsync(new Quotes.QuoteEstimateRequest { Text = message }, explainWithAi: false, cancellationToken);
            var reply = estimate.Explanation
                        + (estimate.Assumptions.Count > 0 ? " Lưu ý: " + string.Join(" ", estimate.Assumptions) : "")
                        + " Bạn có thể chỉnh thông số và gửi yêu cầu để cửa hàng báo giá chính thức tại trang Báo giá (/bao-gia).";
            var similar = await facts.GetFactsAsync(estimate.SimilarProducts.Select(p => p.ProductId).ToList(), cancellationToken);
            return (new Answer(reply, similar.Select(f => f.Id).ToList(), similar.ToDictionary(f => f.Id, _ => "Mẫu có sẵn cùng loại (giá bán lẻ)"),
                ["Dùng gỗ khác rẻ hơn?", "Thời gian đóng bao lâu?", "Chat với nhân viên"], UsedAi: false), similar);
        }
        catch (AppValidationException ex)
        {
            return (new Answer("Mình có thể tính giá dự kiến cho đồ đặt đóng. Bạn cho mình biết loại sản phẩm (bàn, tủ, giường, kệ, sofa...), kích thước "
                               + "dài x rộng x cao và chất liệu mong muốn nhé - hoặc điền nhanh ở trang Báo giá (/bao-gia). (" + string.Join(" ", ex.Errors) + ")",
                [], new Dictionary<int, string>(), ["Báo giá bàn ăn gỗ sồi 1m6", "Báo giá tủ quần áo 1m6 cao 2m", "Báo giá giường gỗ óc chó 1m8"], UsedAi: false), []);
        }
    }

    public async Task<Quotes.QuoteEstimateDto> EstimatePriceAsync(AiCaller caller, Quotes.QuoteEstimateRequest request, CancellationToken cancellationToken = default)
    {
        RequireCaller(caller);
        var estimate = await quoteService.EstimateAsync(request, explainWithAi: true, cancellationToken);

        // Kept in the AI history (customer + admin review) like the other assistant features.
        var question = string.IsNullOrWhiteSpace(request.Text) ? Quotes.QuoteService.Summary(estimate.Spec) : Clip(request.Text.Trim(), MaxMessageLength);
        var conversation = await StartConversationAsync(caller, AIConversationType.PriceQuote, "Báo giá: " + question, null, cancellationToken);
        await conversations.AddMessageAsync(NewMessage(conversation, AIMessageRole.User, question), cancellationToken);
        await SaveAnswerAsync(conversation, new Answer(estimate.Explanation, [], new Dictionary<int, string>(), [], UsedAi: estimate.AiEnabled), cancellationToken);
        return estimate;
    }

    // ================================================================== history

    public Task<IReadOnlyList<AiConversationListItemDto>> GetMyConversationsAsync(AiCaller caller, int take = 30, CancellationToken cancellationToken = default) =>
        caller.IsKnown ? conversations.ListForCallerAsync(caller, Math.Clamp(take, 1, 100), cancellationToken) : Task.FromResult<IReadOnlyList<AiConversationListItemDto>>([]);

    public async Task<AiConversationDto> GetMyConversationAsync(AiCaller caller, int conversationId, CancellationToken cancellationToken = default)
    {
        _ = await conversations.GetOwnedAsync(conversationId, caller, cancellationToken) ?? throw new NotFoundException("cuộc trò chuyện", conversationId);
        return await AiConversationReader.ReadAsync(conversations, facts, conversationId, cancellationToken);
    }

    public Task<int> ClaimAnonymousConversationsAsync(string anonymousId, string userId, CancellationToken cancellationToken = default) =>
        conversations.ClaimAsync(anonymousId, userId, cancellationToken);

    // ================================================================== helpers

    private Answer ReadListAnswer(AiCompletionResult completion, string summaryField, string itemsField, IReadOnlyList<ProductFact> candidates,
        IReadOnlyCollection<decimal> allowedAmounts, Answer fallback, bool keepAll)
    {
        if (!ModelJson.TryParse(completion.Content, out var root))
        {
            return fallback with { UsedAi = true, Completion = completion };
        }

        var ids = new List<int>();
        var reasons = new Dictionary<int, string>(fallback.Reasons);
        foreach (var item in ModelJson.Array(root, itemsField))
        {
            if (ModelJson.Int(item, "id") is int id && candidates.Any(c => c.Id == id) && !ids.Contains(id))
            {
                ids.Add(id);
                if (ModelJson.String(item, "reason") is { Length: > 0 } reason) reasons[id] = Clip(reason, 300);
            }
        }

        if (keepAll || ids.Count == 0)
        {
            // Room sets are chosen by the budget logic; the model only explains them.
            ids = candidates.Select(c => c.Id).ToList();
        }

        var summary = ModelJson.String(root, summaryField);
        return new Answer(
            string.IsNullOrWhiteSpace(summary) ? fallback.Reply : PriceGuard.Sanitize(Clip(summary, 1000), allowedAmounts, out _),
            ids, reasons, fallback.Suggestions, UsedAi: true, Completion: completion);
    }

    private async Task<AIConversation> StartConversationAsync(AiCaller caller, AIConversationType type, string title, int? productId, CancellationToken cancellationToken)
    {
        var conversation = new AIConversation
        {
            UserId = caller.UserId,
            AnonymousId = caller.UserId is null ? caller.AnonymousId : null,
            Type = type,
            Title = Clip(title.Replace('\n', ' '), 200),
            ProductId = productId,
            LastMessageAt = timeProvider.GetUtcNow().UtcDateTime
        };
        await conversations.AddAsync(conversation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    private AIMessage NewMessage(AIConversation conversation, AIMessageRole role, string content) => new()
    {
        ConversationId = conversation.Id,
        Role = role,
        Content = content,
        CreatedAt = timeProvider.GetUtcNow().UtcDateTime
    };

    private async Task SaveAnswerAsync(AIConversation conversation, Answer answer, CancellationToken cancellationToken)
    {
        var message = NewMessage(conversation, AIMessageRole.Assistant, answer.Reply);
        message.IsError = answer.IsError;
        message.Model = answer.Completion?.Model ?? (answer.UsedAi ? null : "rule-based");
        message.PromptTokens = answer.Completion?.PromptTokens;
        message.CompletionTokens = answer.Completion?.CompletionTokens;
        message.MetadataJson = JsonSerializer.Serialize(new AiMessageMetadata(
            answer.ProductIds,
            answer.Reasons.Where(r => answer.ProductIds.Contains(r.Key)).ToDictionary(r => r.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), r => r.Value),
            answer.Suggestions, answer.UsedAi), AiMessageMetadata.Json);
        await conversations.AddMessageAsync(message, cancellationToken);

        conversation.MessageCount += 2;
        conversation.TotalTokens += (answer.Completion?.PromptTokens ?? 0) + (answer.Completion?.CompletionTokens ?? 0);
        conversation.LastMessageAt = message.CreatedAt;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<ProductRecommendationDto> Cards(IReadOnlyList<ProductFact> candidates, Answer answer) =>
        answer.ProductIds
            .Select(id => candidates.FirstOrDefault(c => c.Id == id))
            .Where(p => p is not null)
            .Select(p => ToCard(p!, answer.Reasons.GetValueOrDefault(p!.Id) ?? string.Empty))
            .ToList();

    public static ProductRecommendationDto ToCard(ProductFact p, string reason) => new(
        p.Id, p.Name, reason, p.Price, p.OriginalPrice, p.ImageUrl, p.Url, p.CategoryName, p.InStock, p.AverageRating, VariantSummary(p));

    private static string? VariantSummary(ProductFact p)
    {
        var parts = new List<string>();
        if (p.Variants.Count > 1) parts.Add($"{p.Variants.Count} phiên bản");
        if (p.Colors.Count > 0) parts.Add(string.Join(", ", p.Colors.Take(3)));
        if (p.Sizes.Count > 0) parts.Add(string.Join(" / ", p.Sizes.Take(3).Select(s => s.Name)));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>Why a product fits, built only from catalog facts.</summary>
    public static string ReasonFor(ProductFact p, ShoppingIntent intent)
    {
        var reasons = new List<string>();
        if (intent.MaxBudget is decimal max && p.Price <= max) reasons.Add("trong ngân sách");
        var color = intent.Colors.FirstOrDefault(c => p.ColorSlugs.Contains(c.Slug));
        if (color.Slug is not null) reasons.Add($"có màu {color.Name.ToLowerInvariant()}");
        var material = intent.Materials.FirstOrDefault(m => p.MaterialSlugs.Contains(m.Slug));
        if (material.Slug is not null) reasons.Add(material.Name.ToLowerInvariant());
        if (intent.Styles.Count > 0 && p.StyleName is not null && intent.Styles.Any(s => s.Name == p.StyleName)) reasons.Add($"phong cách {p.StyleName.ToLowerInvariant()}");
        if (intent.LengthMm is int length && p.Sizes.Any(s => Math.Abs(s.LengthMm - length) <= length * 0.15))
        {
            reasons.Add($"có kích thước {p.Sizes.OrderBy(s => Math.Abs(s.LengthMm - length)).First().Name}");
        }

        if (p.ReviewCount > 0 && p.AverageRating >= 4.5m) reasons.Add($"được đánh giá {p.AverageRating:0.0}★");
        if (p.SoldCount >= 50) reasons.Add("bán chạy");
        if (!p.InStock) reasons.Add("tạm hết hàng - có thể đặt trước");
        return reasons.Count == 0 ? p.ShortDescription ?? p.CategoryName : Capitalize(string.Join(", ", reasons));
    }

    /// <summary>Money the AI may mention: catalog prices of the candidates, amounts written by the customer and by store policies.</summary>
    private static IReadOnlyCollection<decimal> AllowedAmounts(IEnumerable<ProductFact> products, IEnumerable<string> customerTexts, IEnumerable<AIKnowledgeEntry> knowledge)
    {
        var amounts = new HashSet<decimal>();
        foreach (var p in products)
        {
            amounts.Add(p.Price);
            amounts.Add(p.MaxPrice);
            if (p.OriginalPrice is decimal original) amounts.Add(original);
            foreach (var v in p.Variants) amounts.Add(v.Price);
        }

        foreach (var text in customerTexts.Concat(knowledge.Select(k => k.Content)))
        {
            foreach (var amount in PriceGuard.AmountsIn(text)) amounts.Add(amount);
            foreach (var amount in PriceGuard.AmountsIn(ShoppingIntentParser.Plain(text))) amounts.Add(amount);
        }

        return amounts;
    }

    /// <summary>
    /// Knowledge entries whose keywords appear as whole words in the question (max 4), most matches first.
    /// Matching is accent-insensitive, so whole words matter: "hỏng" must not match inside "không".
    /// </summary>
    public static IReadOnlyList<AIKnowledgeEntry> SelectKnowledge(IReadOnlyList<AIKnowledgeEntry> all, string message)
    {
        var plain = " " + System.Text.RegularExpressions.Regex.Replace(ShoppingIntentParser.Plain(message), @"[^\p{L}\d]+", " ") + " ";
        return all
            .Select(k => (Entry: k, Hits: (k.Keywords ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ShoppingIntentParser.Plain)
                .Count(keyword => keyword.Length > 1 && plain.Contains(" " + keyword + " ", StringComparison.Ordinal))))
            .Where(x => x.Hits > 0)
            .OrderByDescending(x => x.Hits)
            .ThenBy(x => x.Entry.DisplayOrder)
            .Take(4)
            .Select(x => x.Entry)
            .ToList();
    }

    private static string DescribeRequest(ProductRecommendationRequest r) => Join(
        r.Needs, r.RoomType, r.Size,
        r.RoomAreaM2 is > 0 ? $"phòng {r.RoomAreaM2:0.#}m2" : null,
        r.People is > 0 ? $"{r.People} người" : null,
        r.Colors is { Length: > 0 } ? "màu " + r.Colors : null,
        r.Materials, r.Style is { Length: > 0 } ? "phong cách " + r.Style : null,
        r.Budget is > 0 ? $"ngân sách {r.Budget:0} đồng" : null);

    private static string DescribeRequest(ColorRecommendationRequest r) => Join(
        r.WallColor is { Length: > 0 } ? "tường " + r.WallColor : null,
        r.FloorColor is { Length: > 0 } ? "sàn " + r.FloorColor : null,
        r.RoomType, r.Style is { Length: > 0 } ? "phong cách " + r.Style : null,
        r.Furniture is { Length: > 0 } ? "cần chọn màu cho " + r.Furniture : null, r.Note);

    private static string DescribeRequest(StyleRecommendationRequest r) => Join(
        r.RoomType, r.RoomAreaM2 is > 0 ? $"diện tích {r.RoomAreaM2:0.#}m2" : null,
        r.Budget is > 0 ? $"ngân sách {r.Budget:0} đồng" : null,
        r.Colors is { Length: > 0 } ? "thích màu " + r.Colors : null, r.Purpose, r.Preferences);

    private static string Join(params string?[] parts) =>
        Clip(string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())), MaxMessageLength);

    private static string NormalizeMessage(string? message)
    {
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new AppValidationException("Message", "Vui lòng nhập câu hỏi.");
        }

        if (text.Length > MaxMessageLength)
        {
            throw new AppValidationException("Message", $"Câu hỏi tối đa {MaxMessageLength} ký tự.");
        }

        return new string(text.Where(c => !char.IsControl(c) || c == '\n').ToArray());
    }

    private static void RequireCaller(AiCaller caller)
    {
        if (!caller.IsKnown)
        {
            throw new BusinessRuleException("Không xác định được phiên làm việc. Vui lòng tải lại trang.");
        }
    }

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
