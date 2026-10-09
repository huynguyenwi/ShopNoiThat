using System.Diagnostics;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers;

/// <summary>
/// Renders friendly error pages. Reached only through middleware re-execution:
/// "/Error" from UseExceptionHandler and "/Error/{code}" from UseStatusCodePagesWithReExecute.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[ApiExplorerSettings(IgnoreApi = true)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorController(IWebHostEnvironment environment) : Controller
{
    [Route("Error")]
    public IActionResult Exception()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (feature is null)
        {
            // Requested directly instead of being re-executed by the exception handler middleware.
            return RenderError(StatusCodes.Status404NotFound, null, null);
        }

        var statusCode = Response.StatusCode >= 400 ? Response.StatusCode : StatusCodes.Status500InternalServerError;

        // Only expected business errors expose their message; unexpected errors show a generic text.
        var message = feature.Error is AppException appException ? appException.Message : null;
        var details = environment.IsDevelopment() ? feature.Error.ToString() : null;

        return RenderError(statusCode, message, details);
    }

    [Route("Error/{statusCode:int}")]
    public IActionResult Status([FromRoute] int statusCode) // not from the form: a re-executed POST may carry an unreadable body
    {
        var reExecuteFeature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        if (reExecuteFeature is null)
        {
            // The page was requested directly (e.g. /Error/500) rather than re-executed by middleware.
            statusCode = StatusCodes.Status404NotFound;
        }
        else
        {
            // The code the middleware re-executed for: the route value was seen missing (0) for an early 413 under load.
            statusCode = reExecuteFeature.OriginalStatusCode;
        }

        var message = statusCode == StatusCodes.Status400BadRequest && AntiforgeryFailureFilter.Failed(HttpContext)
            ? AntiforgeryFailureFilter.Message
            : null;

        return RenderError(statusCode, message, null);
    }

    private ViewResult RenderError(int statusCode, string? message, string? details)
    {
        Response.StatusCode = statusCode;
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View("Error", ErrorViewModel.For(statusCode, message, requestId, details));
    }
}
