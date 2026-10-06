using FurnitureStore.Application.AI;
using FurnitureStore.Application.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers;

public sealed record AdvisorViewModel(FilterOptionsDto Options, bool AiEnabled);

/// <summary>/tu-van - AI tools: product suggestions for a room, color matching and style selection.</summary>
[Route("tu-van")]
public sealed class AdvisorController(ICatalogService catalog, IAssistantService assistant) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new AdvisorViewModel(await catalog.GetFilterOptionsAsync(cancellationToken), assistant.AiEnabled));
}
