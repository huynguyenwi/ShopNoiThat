using FurnitureStore.Application.Quotes;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FurnitureStore.Web.Controllers.Api;

/// <summary>
/// Custom-furniture quote requests. Guests can submit (the shop calls them back); signed-in customers can follow,
/// accept, reject or cancel their own requests. Prices are always recomputed on the server.
/// </summary>
[Route("api/quotes")]
public sealed class QuoteApiController(IQuoteService quotes) : ApiControllerBase
{
    /// <summary>POST /api/quotes { text, kind, sizes, materialId, finish, quantity, name, phone, email, note }</summary>
    [HttpPost("")]
    [EnableRateLimiting(RateLimitPolicies.Forms)]
    public async Task<IActionResult> Submit([FromBody] QuoteSubmitCommand command, CancellationToken cancellationToken) =>
        OkResponse(await quotes.SubmitAsync(command, User.UserId(), cancellationToken), "Đã gửi yêu cầu báo giá. Cửa hàng sẽ liên hệ với bạn sớm.");

    /// <summary>GET /api/quotes - the signed-in customer's requests.</summary>
    [HttpGet("")]
    [Authorize(Policy = AuthorizationPolicies.SignedIn)]
    public async Task<IActionResult> Mine([FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        OkResponse(await quotes.ListMineAsync(User.UserId()!, page, cancellationToken));

    /// <summary>GET /api/quotes/{code} - 404 for another customer's code.</summary>
    [HttpGet("{code}")]
    [Authorize(Policy = AuthorizationPolicies.SignedIn)]
    public async Task<IActionResult> Get(string code, CancellationToken cancellationToken) =>
        OkResponse(await quotes.GetMineAsync(User.UserId()!, code, cancellationToken));

    [HttpPost("{code}/accept")]
    [Authorize(Policy = AuthorizationPolicies.SignedIn)]
    public async Task<IActionResult> Accept(string code, CancellationToken cancellationToken)
    {
        await quotes.RespondAsync(User.UserId()!, code, accept: true, cancellationToken);
        return OkResponse("Bạn đã đồng ý báo giá. Cửa hàng sẽ liên hệ để lên lịch sản xuất.");
    }

    [HttpPost("{code}/reject")]
    [Authorize(Policy = AuthorizationPolicies.SignedIn)]
    public async Task<IActionResult> Reject(string code, CancellationToken cancellationToken)
    {
        await quotes.RespondAsync(User.UserId()!, code, accept: false, cancellationToken);
        return OkResponse("Đã ghi nhận bạn từ chối báo giá.");
    }

    [HttpPost("{code}/cancel")]
    [Authorize(Policy = AuthorizationPolicies.SignedIn)]
    public async Task<IActionResult> Cancel(string code, CancellationToken cancellationToken)
    {
        await quotes.CancelMineAsync(User.UserId()!, code, cancellationToken);
        return OkResponse("Đã hủy yêu cầu báo giá.");
    }
}
