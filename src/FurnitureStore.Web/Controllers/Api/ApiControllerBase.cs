using FurnitureStore.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Controllers.Api;

/// <summary>
/// Base class for JSON API controllers. Every response uses the ApiResponse envelope; validation errors
/// and exceptions are converted by ApiBehaviorOptions / GlobalExceptionHandler.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected OkObjectResult OkResponse<T>(T data, string message = "Thành công") => Ok(ApiResponse.Ok(data, message));

    protected OkObjectResult OkResponse(string message = "Thành công") => Ok(ApiResponse.Ok(message));
}
