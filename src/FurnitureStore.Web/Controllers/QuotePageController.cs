using FurnitureStore.Application.AI;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Quotes;
using FurnitureStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers;

public sealed record QuotePageViewModel(
    IReadOnlyList<QuoteKind> Kinds,
    FilterOptionsDto Options,
    bool AiEnabled,
    string? Name,
    string? Phone,
    string? Email,
    string? InitialText);

/// <summary>/bao-gia - custom furniture: describe it, get an estimate from the price calculator, send the request.</summary>
[Route("bao-gia")]
public sealed class QuotePageController(ICatalogService catalog, IAssistantService assistant, UserManager<ApplicationUser> userManager) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken cancellationToken)
    {
        var options = await catalog.GetFilterOptionsAsync(cancellationToken);
        var user = User.Identity?.IsAuthenticated == true ? await userManager.GetUserAsync(User) : null;
        return View(new QuotePageViewModel(QuoteKinds.From(options), options, assistant.AiEnabled,
            user?.FullName, user?.PhoneNumber, user?.Email, q is { Length: <= 500 } ? q : null));
    }
}
