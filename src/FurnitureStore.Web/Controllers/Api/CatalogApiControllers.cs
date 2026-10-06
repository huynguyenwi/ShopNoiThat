using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Web.ViewModels.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers.Api;

[Route("api/products")]
public sealed class ProductsApiController(ICatalogService catalog) : ApiControllerBase
{
    /// <summary>GET /api/products?q=&amp;category=&amp;color=&amp;minPrice=&amp;sort=&amp;page=</summary>
    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] ProductListRequest request, CancellationToken cancellationToken) =>
        OkResponse(await catalog.GetProductsAsync(request.ToQuery(), cancellationToken));

    /// <summary>GET /api/products/{id}</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        OkResponse(await catalog.GetProductByIdAsync(id, cancellationToken) ?? throw new NotFoundException("sản phẩm", id));

    /// <summary>GET /api/products/search?q=ban go&amp;limit=8 - autocomplete suggestions.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int limit = 8, CancellationToken cancellationToken = default) =>
        OkResponse(await catalog.SuggestAsync(q, limit, cancellationToken));
}

[Route("api/categories")]
public sealed class CategoriesApiController(ICatalogService catalog) : ApiControllerBase
{
    /// <summary>GET /api/categories - active category tree with product counts.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Tree(CancellationToken cancellationToken) =>
        OkResponse(await catalog.GetCategoryTreeAsync(cancellationToken));
}
