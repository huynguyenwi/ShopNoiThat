using FurnitureStore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers;

/// <summary>GET /images/placeholder/{shape}.svg?color=5c4033&amp;bg=f1e9dd - generated product illustrations.</summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class PlaceholderController : Controller
{
    [HttpGet("images/placeholder/{shape}.svg")]
    [ResponseCache(Duration = 31_536_000, Location = ResponseCacheLocation.Any)]
    public IActionResult Render(string shape, string? color, string? bg)
    {
        var svg = PlaceholderSvgRenderer.Render(shape, color?.ToLowerInvariant(), bg?.ToLowerInvariant());
        if (svg is null)
        {
            return NotFound();
        }

        // Defense in depth: an SVG opened directly must never run scripts.
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";
        return Content(svg, "image/svg+xml; charset=utf-8");
    }
}
