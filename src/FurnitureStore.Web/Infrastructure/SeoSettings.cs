namespace FurnitureStore.Web.Infrastructure;

/// <summary>Bound from the "Seo" section.</summary>
public sealed class SeoSettings
{
    public const string SectionName = "Seo";

    /// <summary>False on staging / test servers: robots.txt disallows everything and every page is noindex.</summary>
    public bool AllowIndexing { get; set; } = true;

    /// <summary>Image shared on social networks when a page has no suitable (raster) image of its own.</summary>
    public string DefaultOgImage { get; set; } = "/images/og-default.png";

    /// <summary>
    /// noindex for private areas (account, cart, checkout, admin...) and when indexing is disabled;
    /// otherwise the page's own value (ViewData["Robots"]) or index,follow.
    /// </summary>
    public string RobotsFor(HttpContext context, string? pageValue)
    {
        if (!AllowIndexing)
        {
            return "noindex,nofollow";
        }

        var path = context.Request.Path;
        if (Controllers.SeoController.PrivatePaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            return "noindex,nofollow";
        }

        return pageValue ?? "index,follow";
    }
}
