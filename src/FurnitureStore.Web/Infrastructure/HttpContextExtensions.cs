using Microsoft.AspNetCore.Diagnostics;

namespace FurnitureStore.Web.Infrastructure;

public static class HttpContextExtensions
{
    /// <summary>
    /// True for requests that expect JSON: /api/* endpoints and AJAX calls made by the site's own scripts.
    /// </summary>
    public static bool IsApiRequest(this HttpContext context)
    {
        var request = context.Request;

        // While an exception is being handled, Request.Path is already rewritten to the error page ("/Error");
        // the original path is kept in IExceptionHandlerPathFeature.
        var path = context.Features.Get<IExceptionHandlerPathFeature>()?.Path is { Length: > 0 } originalPath
            ? new PathString(originalPath)
            : request.Path;

        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var accept = request.Headers.Accept.ToString();
        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
               && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }
}
