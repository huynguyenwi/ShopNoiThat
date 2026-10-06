using FurnitureStore.Application.Catalog.Admin;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers.Api;

/// <summary>Admin product API. Every action requires the ADMIN role (enforced on the server).</summary>
[Route("api/admin/products")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminProductsApiController(IProductAdminService products) : ApiControllerBase
{
    /// <summary>GET /api/admin/products?search=&amp;categoryId=&amp;status=&amp;stock=&amp;page=</summary>
    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] AdminProductQuery query, CancellationToken cancellationToken) =>
        OkResponse(await products.ListAsync(query, cancellationToken));

    /// <summary>GET /api/admin/products/{id} - editable payload (includes the concurrency version).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        OkResponse(await products.GetForEditAsync(id, cancellationToken));

    /// <summary>POST /api/admin/products</summary>
    [HttpPost("")]
    public async Task<IActionResult> Create([FromBody] ProductUpsertCommand command, CancellationToken cancellationToken)
    {
        var saved = await products.CreateAsync(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, Application.Common.Models.ApiResponse.Ok(saved, "Đã tạo sản phẩm."));
    }

    /// <summary>PUT /api/admin/products/{id}</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductUpsertCommand command, CancellationToken cancellationToken) =>
        OkResponse(await products.UpdateAsync(id, command, cancellationToken), "Đã cập nhật sản phẩm.");

    /// <summary>DELETE /api/admin/products/{id} (soft delete)</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await products.DeleteAsync(id, cancellationToken);
        return OkResponse("Đã xóa sản phẩm.");
    }
}
