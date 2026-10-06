using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Sales;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers.Api;

/// <summary>
/// Vietnamese administrative units for address forms: province → ward / commune (two levels since 07/2025).
/// Served from data built into the application, so address forms never depend on an outside service. Public and cacheable.
/// </summary>
[Route("api/locations")]
[ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
public sealed class LocationsApiController : ApiControllerBase
{
    /// <summary>GET /api/locations/provinces - the 34 provinces / cities, cities first.</summary>
    [HttpGet("provinces")]
    public IActionResult Provinces() =>
        OkResponse(VietnamProvinces.Provinces.Select(p => new { p.Code, p.Name, WardCount = p.Wards.Count }));

    /// <summary>GET /api/locations/provinces/{code}/wards - wards, then communes, then special zones, alphabetically.</summary>
    [HttpGet("provinces/{code:int}/wards")]
    public IActionResult Wards(int code)
    {
        var province = VietnamProvinces.FindByCode(code) ?? throw new NotFoundException("tỉnh / thành phố", code);
        return OkResponse(province.Wards.Select(w => new { w.Code, w.Name, w.Type, w.Label }));
    }
}
