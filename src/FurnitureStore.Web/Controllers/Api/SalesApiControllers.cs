using System.ComponentModel.DataAnnotations;
using FurnitureStore.Application.Sales;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers.Api;

public sealed class AddToCartRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn phiên bản sản phẩm.")]
    public int VariantId { get; set; }

    [Range(1, 99, ErrorMessage = "Số lượng phải từ 1 đến 99.")]
    public int Quantity { get; set; } = 1;
}

public sealed class UpdateCartItemRequest
{
    [Range(0, 99, ErrorMessage = "Số lượng phải từ 0 đến 99.")]
    public int Quantity { get; set; }
}

public sealed class CouponRequest
{
    [Required(ErrorMessage = "Vui lòng nhập mã giảm giá.")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;
}

public sealed class CancelOrderRequest
{
    [StringLength(500)]
    public string? Reason { get; set; }
}

/// <summary>Cart API for the site's own scripts (cookie auth + anti-forgery header). Works for guests too.</summary>
[Route("api/cart")]
public sealed class CartApiController(ICartService cart, CartOwnerResolver owners) : ApiControllerBase
{
    /// <summary>GET /api/cart</summary>
    [HttpGet("")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        OkResponse(await cart.GetAsync(owners.Resolve(), cancellationToken));

    /// <summary>GET /api/cart/count - number of items for the header badge.</summary>
    [HttpGet("count")]
    public async Task<IActionResult> Count(CancellationToken cancellationToken) =>
        OkResponse(new { count = await cart.CountAsync(owners.Resolve(), cancellationToken) });

    /// <summary>POST /api/cart { variantId, quantity }</summary>
    [HttpPost("")]
    public async Task<IActionResult> Add([FromBody] AddToCartRequest request, CancellationToken cancellationToken) =>
        OkResponse(await cart.AddAsync(owners.Resolve(createIfMissing: true), request.VariantId, request.Quantity, cancellationToken), "Đã thêm vào giỏ hàng.");

    /// <summary>PUT /api/cart/items/{id} { quantity } - 0 removes the item.</summary>
    [HttpPut("items/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCartItemRequest request, CancellationToken cancellationToken) =>
        OkResponse(await cart.UpdateQuantityAsync(owners.Resolve(), id, request.Quantity, cancellationToken), "Đã cập nhật giỏ hàng.");

    /// <summary>DELETE /api/cart/items/{id}</summary>
    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken cancellationToken) =>
        OkResponse(await cart.RemoveAsync(owners.Resolve(), id, cancellationToken), "Đã xóa sản phẩm khỏi giỏ hàng.");

    /// <summary>POST /api/cart/coupon { code }</summary>
    [HttpPost("coupon")]
    public async Task<IActionResult> ApplyCoupon([FromBody] CouponRequest request, CancellationToken cancellationToken) =>
        OkResponse(await cart.ApplyCouponAsync(owners.Resolve(), request.Code, cancellationToken), "Đã áp dụng mã giảm giá.");

    /// <summary>DELETE /api/cart/coupon</summary>
    [HttpDelete("coupon")]
    public async Task<IActionResult> RemoveCoupon(CancellationToken cancellationToken) =>
        OkResponse(await cart.RemoveCouponAsync(owners.Resolve(), cancellationToken), "Đã bỏ mã giảm giá.");
}

/// <summary>Orders of the signed-in customer. Other customers' orders are never visible (404).</summary>
[Route("api/orders")]
[Authorize(Policy = AuthorizationPolicies.SignedIn)]
public sealed class OrdersApiController(IOrderService orders) : ApiControllerBase
{
    private string UserId => User.UserId()!;

    /// <summary>POST /api/orders - places an order from the current cart.</summary>
    [HttpPost("")]
    public async Task<IActionResult> Place([FromBody] CheckoutCommand command, CancellationToken cancellationToken)
    {
        var result = await orders.PlaceOrderAsync(UserId, command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, Application.Common.Models.ApiResponse.Ok(result, "Đặt hàng thành công."));
    }

    /// <summary>GET /api/orders?page=1</summary>
    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        OkResponse(await orders.GetMyOrdersAsync(UserId, page, 10, cancellationToken));

    /// <summary>GET /api/orders/{id}</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        OkResponse(await orders.GetMyOrderAsync(UserId, id, cancellationToken));

    /// <summary>POST /api/orders/{id}/cancel { reason }</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await orders.GetMyOrderAsync(UserId, id, cancellationToken);
        await orders.CancelMyOrderAsync(UserId, order.OrderCode, request.Reason, cancellationToken);
        return OkResponse(await orders.GetMyOrderAsync(UserId, id, cancellationToken), "Đã hủy đơn hàng.");
    }
}

[Route("api/wishlist")]
[Authorize(Policy = AuthorizationPolicies.SignedIn)]
public sealed class WishlistApiController(IWishlistService wishlist) : ApiControllerBase
{
    private string UserId => User.UserId()!;

    /// <summary>GET /api/wishlist - products in the wishlist.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        OkResponse(await wishlist.GetAsync(UserId, cancellationToken));

    /// <summary>GET /api/wishlist/ids - product ids, used to highlight heart buttons.</summary>
    [HttpGet("ids")]
    public async Task<IActionResult> Ids(CancellationToken cancellationToken) =>
        OkResponse(await wishlist.GetProductIdsAsync(UserId, cancellationToken));

    /// <summary>POST /api/wishlist/{productId}/toggle</summary>
    [HttpPost("{productId:int}/toggle")]
    public async Task<IActionResult> Toggle(int productId, CancellationToken cancellationToken)
    {
        var added = await wishlist.ToggleAsync(UserId, productId, cancellationToken);
        return OkResponse(new { productId, inWishlist = added }, added ? "Đã thêm vào danh sách yêu thích." : "Đã bỏ khỏi danh sách yêu thích.");
    }

    /// <summary>DELETE /api/wishlist/{productId}</summary>
    [HttpDelete("{productId:int}")]
    public async Task<IActionResult> Remove(int productId, CancellationToken cancellationToken)
    {
        await wishlist.RemoveAsync(UserId, productId, cancellationToken);
        return OkResponse("Đã bỏ khỏi danh sách yêu thích.");
    }
}
