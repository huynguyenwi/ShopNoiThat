using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Quotes;
using FurnitureStore.Domain.Exceptions;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers;

/// <summary>/account/quotes - the customer's custom-furniture quote requests (view, accept / reject a price, cancel).</summary>
[Authorize(Policy = AuthorizationPolicies.SignedIn)]
[Route("account/quotes")]
public sealed class AccountQuotesController(IQuoteService quotes) : Controller
{
    public const string MessageKey = "QuoteMessage";

    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default) =>
        View(await quotes.ListMineAsync(User.UserId()!, page, cancellationToken));

    [HttpGet("{code}")]
    public async Task<IActionResult> Details(string code, CancellationToken cancellationToken) =>
        View(await quotes.GetMineAsync(User.UserId()!, code, cancellationToken));

    [HttpPost("{code}/accept")]
    public Task<IActionResult> Accept(string code, CancellationToken cancellationToken) =>
        RunAsync(code, () => quotes.RespondAsync(User.UserId()!, code, true, cancellationToken), "Cảm ơn bạn đã đồng ý báo giá! Cửa hàng sẽ liên hệ để lên lịch sản xuất.");

    [HttpPost("{code}/reject")]
    public Task<IActionResult> Reject(string code, CancellationToken cancellationToken) =>
        RunAsync(code, () => quotes.RespondAsync(User.UserId()!, code, false, cancellationToken), "Đã ghi nhận bạn từ chối báo giá.");

    [HttpPost("{code}/cancel")]
    public Task<IActionResult> Cancel(string code, CancellationToken cancellationToken) =>
        RunAsync(code, () => quotes.CancelMineAsync(User.UserId()!, code, cancellationToken), "Đã hủy yêu cầu báo giá.");

    private async Task<IActionResult> RunAsync(string code, Func<Task> action, string message)
    {
        try
        {
            await action();
            TempData[MessageKey] = message;
        }
        catch (Exception ex) when (ex is BusinessRuleException or DomainException)
        {
            TempData[MessageKey] = "Lỗi: " + ex.Message;
        }

        return Redirect($"/account/quotes/{code.ToUpperInvariant()}");
    }
}
