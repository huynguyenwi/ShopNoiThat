using FurnitureStore.Application.AI;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers;

/// <summary>/account/ai-history - the signed-in customer's conversations with the AI assistant.</summary>
[Authorize(Policy = AuthorizationPolicies.SignedIn)]
[Route("account/ai-history")]
public sealed class AccountAiController(IAssistantService assistant) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await assistant.GetMyConversationsAsync(Caller(), 50, cancellationToken));

    /// <summary>Another customer's conversation id gives 404 (the service checks ownership).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken) =>
        View(await assistant.GetMyConversationAsync(Caller(), id, cancellationToken));

    private AiCaller Caller() => new(User.UserId(), null);
}
