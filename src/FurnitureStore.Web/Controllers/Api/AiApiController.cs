using FurnitureStore.Application.AI;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FurnitureStore.Web.Controllers.Api;

/// <summary>
/// AI assistant API. Works for guests too; every answer is grounded on catalog data and rate-limited per user / IP
/// (AI:RequestsPerMinute). When no API key is configured the rule-based assistant answers instead.
/// </summary>
[Route("api/ai")]
[EnableRateLimiting(RateLimitPolicies.Ai)]
public sealed class AiApiController(IAssistantService assistant, TimeProvider timeProvider) : ApiControllerBase
{
    /// <summary>GET /api/ai/status - whether an AI model is configured (the UI labels rule-based answers).</summary>
    [HttpGet("status")]
    [DisableRateLimiting]
    public IActionResult Status() => OkResponse(new { aiEnabled = assistant.AiEnabled, signedIn = User.Identity?.IsAuthenticated == true });

    /// <summary>POST /api/ai/chat { conversationId, message, productId }</summary>
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] AiChatRequest request, CancellationToken cancellationToken) =>
        OkResponse(await assistant.ChatAsync(Caller(), request, cancellationToken));

    /// <summary>POST /api/ai/recommend { budget, roomType, roomAreaM2, people, colors, materials, style, size, needs }</summary>
    [HttpPost("recommend")]
    public async Task<IActionResult> Recommend([FromBody] ProductRecommendationRequest request, CancellationToken cancellationToken) =>
        OkResponse(await assistant.RecommendProductsAsync(Caller(), request, cancellationToken));

    /// <summary>POST /api/ai/color-recommend { wallColor, floorColor, roomType, style, furniture, note }</summary>
    [HttpPost("color-recommend")]
    public async Task<IActionResult> ColorRecommend([FromBody] ColorRecommendationRequest request, CancellationToken cancellationToken) =>
        OkResponse(await assistant.RecommendColorsAsync(Caller(), request, cancellationToken));

    /// <summary>POST /api/ai/style-recommend { roomAreaM2, budget, roomType, colors, purpose, preferences }</summary>
    [HttpPost("style-recommend")]
    public async Task<IActionResult> StyleRecommend([FromBody] StyleRecommendationRequest request, CancellationToken cancellationToken) =>
        OkResponse(await assistant.RecommendStylesAsync(Caller(), request, cancellationToken));

    /// <summary>
    /// POST /api/ai/price-estimate { text, kind, lengthMm, widthMm, heightMm, materialId, colorId, styleId, finish, quantity } -
    /// custom-furniture estimate: the price comes from the price calculator (PriceRules), the AI only reads the request and explains.
    /// </summary>
    [HttpPost("price-estimate")]
    public async Task<IActionResult> PriceEstimate([FromBody] Application.Quotes.QuoteEstimateRequest request, CancellationToken cancellationToken) =>
        OkResponse(await assistant.EstimatePriceAsync(Caller(), request, cancellationToken));

    /// <summary>GET /api/ai/conversations - the visitor's own AI conversations.</summary>
    [HttpGet("conversations")]
    [DisableRateLimiting]
    public async Task<IActionResult> Conversations(CancellationToken cancellationToken) =>
        OkResponse(await assistant.GetMyConversationsAsync(AiVisitor.Resolve(HttpContext), 30, cancellationToken));

    /// <summary>GET /api/ai/conversations/{id} - only the owner can read it (404 otherwise).</summary>
    [HttpGet("conversations/{id:int}")]
    [DisableRateLimiting]
    public async Task<IActionResult> Conversation(int id, CancellationToken cancellationToken) =>
        OkResponse(await assistant.GetMyConversationAsync(AiVisitor.Resolve(HttpContext), id, cancellationToken));

    private AiCaller Caller() => AiVisitor.Resolve(HttpContext, timeProvider);
}
