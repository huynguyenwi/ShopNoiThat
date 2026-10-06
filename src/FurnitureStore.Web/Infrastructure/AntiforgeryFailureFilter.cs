using FurnitureStore.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;

namespace FurnitureStore.Web.Infrastructure;

/// <summary>
/// A rejected anti-forgery token almost always means a stale page (opened long ago, or the user signed in / out in
/// another tab). Tell the user what to do: the ApiResponse envelope for API calls, the 400 error page for forms.
/// Runs before [ApiController]'s ProblemDetails mapping (order -2000).
/// </summary>
public sealed class AntiforgeryFailureFilter : IAlwaysRunResultFilter, IOrderedFilter
{
    public const string Message = "Phiên làm việc đã hết hạn. Vui lòng tải lại trang rồi thử lại.";
    private const string ItemKey = "AntiforgeryFailed";

    public int Order => -3000;

    public static bool Failed(HttpContext context) => context.Items.ContainsKey(ItemKey);

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not IAntiforgeryValidationFailedResult)
        {
            return;
        }

        if (context.HttpContext.IsApiRequest())
        {
            context.Result = new BadRequestObjectResult(ApiResponse.Fail(Message));
        }
        else
        {
            context.HttpContext.Items[ItemKey] = true; // read by ErrorController when the 400 page is re-executed
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
