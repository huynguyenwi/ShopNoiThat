using FurnitureStore.Application.Common.Models;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.AI;

// ================================================================== who is asking

/// <summary>The visitor using the assistant: a signed-in user or a guest identified by a random cookie id.</summary>
public sealed record AiCaller(string? UserId, string? AnonymousId)
{
    public bool IsKnown => UserId is not null || AnonymousId is not null;
}

// ================================================================== product cards returned by every AI feature

/// <summary>
/// A product suggested by the assistant. Everything except <see cref="Reason"/> comes from the database,
/// so the price shown to the customer is always the real catalog price.
/// </summary>
public sealed record ProductRecommendationDto(
    int ProductId,
    string Name,
    string Reason,
    decimal Price,
    decimal? OriginalPrice,
    string? ImageUrl,
    string Url,
    string CategoryName,
    bool InStock,
    decimal AverageRating,
    string? VariantSummary);

// ================================================================== chatbot

public sealed class AiChatRequest
{
    public int? ConversationId { get; set; }
    public string? Message { get; set; }

    /// <summary>Product page the question is about ("AI tư vấn sản phẩm này").</summary>
    public int? ProductId { get; set; }
}

public sealed record AiChatResponse(
    int ConversationId,
    string Reply,
    IReadOnlyList<ProductRecommendationDto> Products,
    IReadOnlyList<string> Suggestions,
    bool AiEnabled);

// ================================================================== structured advice

public sealed class ProductRecommendationRequest
{
    public decimal? Budget { get; set; }
    public string? RoomType { get; set; }
    public decimal? RoomAreaM2 { get; set; }
    public int? People { get; set; }
    public string? Colors { get; set; }
    public string? Materials { get; set; }
    public string? Style { get; set; }
    public string? Size { get; set; }
    public string? Needs { get; set; }
}

public sealed record ProductRecommendationResponse(
    string Summary,
    IReadOnlyList<ProductRecommendationDto> Items,
    decimal? TotalPrice,
    bool AiEnabled,
    int? ConversationId);

public sealed class ColorRecommendationRequest
{
    /// <summary>e.g. "Tường trắng", "xám nhạt".</summary>
    public string? WallColor { get; set; }
    public string? FloorColor { get; set; }
    public string? RoomType { get; set; }
    public string? Style { get; set; }

    /// <summary>What they want to buy, e.g. "bàn ăn và ghế".</summary>
    public string? Furniture { get; set; }
    public string? Note { get; set; }
}

public sealed record ColorSuggestionDto(string Name, string Slug, string Hex, string Reason);

public sealed record FurnitureColorAdviceDto(string Furniture, IReadOnlyList<ColorSuggestionDto> Colors);

public sealed record ColorRecommendationResponse(
    string Summary,
    IReadOnlyList<FurnitureColorAdviceDto> Advice,
    IReadOnlyList<ProductRecommendationDto> Products,
    bool AiEnabled,
    int? ConversationId);

public sealed class StyleRecommendationRequest
{
    public decimal? RoomAreaM2 { get; set; }
    public decimal? Budget { get; set; }
    public string? RoomType { get; set; }
    public string? Colors { get; set; }
    public string? Purpose { get; set; }
    public string? Preferences { get; set; }
}

public sealed record StyleSuggestionDto(string Name, string Slug, string Description, string Reason, IReadOnlyList<string> KeyPoints);

public sealed record StyleRecommendationResponse(
    string Summary,
    IReadOnlyList<StyleSuggestionDto> Styles,
    IReadOnlyList<ProductRecommendationDto> Products,
    bool AiEnabled,
    int? ConversationId);

// ================================================================== history

public sealed record AiConversationListItemDto(
    int Id,
    AIConversationType Type,
    string Title,
    string? UserId,
    string? UserName,
    bool IsGuest,
    string? ProductName,
    int MessageCount,
    int TotalTokens,
    bool HasErrors,
    DateTime CreatedAt,
    DateTime LastMessageAt);

public sealed record AiMessageDto(
    int Id,
    AIMessageRole Role,
    string Content,
    IReadOnlyList<ProductRecommendationDto> Products,
    IReadOnlyList<string> Suggestions,
    bool IsError,
    string? Model,
    int? PromptTokens,
    int? CompletionTokens,
    DateTime CreatedAt);

public sealed record AiConversationDto(AiConversationListItemDto Conversation, IReadOnlyList<AiMessageDto> Messages);

public sealed class AdminAiConversationQuery
{
    public string? Search { get; set; }
    public AIConversationType? Type { get; set; }
    public bool ErrorsOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

// ================================================================== service contract

/// <summary>
/// The shop assistant ("IAIService" in the specification). Works with an OpenAI-compatible model when an API key is
/// configured, and falls back to a rule-based assistant otherwise. Product data always comes from the database.
/// Price estimates for custom furniture come from the price calculator; the assistant only explains them.
/// </summary>
public interface IAssistantService
{
    bool AiEnabled { get; }

    Task<AiChatResponse> ChatAsync(AiCaller caller, AiChatRequest request, CancellationToken cancellationToken = default);
    Task<ProductRecommendationResponse> RecommendProductsAsync(AiCaller caller, ProductRecommendationRequest request, CancellationToken cancellationToken = default);
    Task<ColorRecommendationResponse> RecommendColorsAsync(AiCaller caller, ColorRecommendationRequest request, CancellationToken cancellationToken = default);
    Task<StyleRecommendationResponse> RecommendStylesAsync(AiCaller caller, StyleRecommendationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Custom-furniture estimate: the price comes from the price calculator (PriceRules), the AI only reads the request and explains.</summary>
    Task<Quotes.QuoteEstimateDto> EstimatePriceAsync(AiCaller caller, Quotes.QuoteEstimateRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiConversationListItemDto>> GetMyConversationsAsync(AiCaller caller, int take = 30, CancellationToken cancellationToken = default);
    Task<AiConversationDto> GetMyConversationAsync(AiCaller caller, int conversationId, CancellationToken cancellationToken = default);

    /// <summary>After sign-in, conversations started as a guest move to the account.</summary>
    Task<int> ClaimAnonymousConversationsAsync(string anonymousId, string userId, CancellationToken cancellationToken = default);
}

// ================================================================== LLM abstraction

public sealed record AiChatMessage(AIMessageRole Role, string Content);

public sealed record AiCompletionRequest(IReadOnlyList<AiChatMessage> Messages, bool JsonResponse = true);

public sealed record AiCompletionResult(string Content, string? Model, int? PromptTokens, int? CompletionTokens);

/// <summary>Chat-completion client of an AI provider (OpenAI or any OpenAI-compatible API).</summary>
public interface IAiChatClient
{
    /// <summary>True when an API key is configured and the feature is enabled.</summary>
    bool IsConfigured { get; }

    /// <exception cref="AiUnavailableException">The provider could not be reached or rejected the request.</exception>
    Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The AI provider failed (timeout, invalid key, quota...). The message is safe to show to users.</summary>
public sealed class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

// ================================================================== persistence

public interface IAiConversationRepository : Common.Interfaces.IRepository<AIConversation>
{
    Task<AIConversation?> GetOwnedAsync(int id, AiCaller caller, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiChatMessage>> GetRecentMessagesAsync(int conversationId, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetRecentUserMessagesAsync(int conversationId, int take, CancellationToken cancellationToken = default);
    Task AddMessageAsync(AIMessage message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiConversationListItemDto>> ListForCallerAsync(AiCaller caller, int take, CancellationToken cancellationToken = default);
    Task<PagedResult<AiConversationListItemDto>> SearchAsync(AdminAiConversationQuery query, CancellationToken cancellationToken = default);
    Task<AiConversationListItemDto?> GetListItemAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AIMessage>> GetMessagesAsync(int conversationId, CancellationToken cancellationToken = default);
    Task<int> ClaimAsync(string anonymousId, string userId, CancellationToken cancellationToken = default);
}

/// <summary>Facts about one product given to the assistant (all from the database).</summary>
public sealed record ProductFact(
    int Id,
    string Name,
    string Slug,
    string CategoryName,
    string CategorySlug,
    string? RoomName,
    string? StyleName,
    FurnitureType FurnitureType,
    string? ShortDescription,
    decimal Price,
    decimal MaxPrice,
    decimal? OriginalPrice,
    bool InStock,
    decimal AverageRating,
    int ReviewCount,
    int SoldCount,
    string? ImageUrl,
    IReadOnlyList<string> Colors,
    IReadOnlyList<string> ColorSlugs,
    IReadOnlyList<string> Materials,
    IReadOnlyList<string> MaterialSlugs,
    IReadOnlyList<ProductFactSize> Sizes,
    IReadOnlyList<ProductFactVariant> Variants)
{
    public string Url => $"/products/{Slug}";
}

public sealed record ProductFactSize(string Name, int LengthMm, int WidthMm, int HeightMm);

public sealed record ProductFactVariant(int Id, string Name, decimal Price, bool InStock);

public interface IProductFactRepository
{
    Task<IReadOnlyList<ProductFact>> GetFactsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken = default);
}

public interface IAiKnowledgeRepository : Common.Interfaces.IRepository<AIKnowledgeEntry>
{
    Task<IReadOnlyList<AIKnowledgeEntry>> GetActiveAsync(CancellationToken cancellationToken = default);
}
