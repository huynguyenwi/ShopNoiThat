using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FurnitureStore.Web.Infrastructure;

/// <summary>
/// Refuses an upload larger than its endpoint's [RequestSizeLimit] from the Content-Length header, before anything reads
/// the body (anti-forgery validation reads the form first). Answers 413 the same way whatever the host: Kestrel, IIS or a
/// proxy / test server that does not enforce request size limits itself. Pages then show "Tệp quá lớn", APIs a JSON error.
/// </summary>
public sealed class PayloadTooLargeFilter : IAuthorizationFilter, IOrderedFilter
{
    /// <summary>Before the anti-forgery filter (1000) and the request size / form limit filters (900).</summary>
    public int Order => 800;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var limit = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize;
        if (limit is long max && context.HttpContext.Request.ContentLength > max)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
        }
    }
}
