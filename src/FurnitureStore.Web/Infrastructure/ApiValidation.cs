using FurnitureStore.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FurnitureStore.Web.Infrastructure;

public static class ApiValidation
{
    /// <summary>
    /// Replaces the default ProblemDetails body of [ApiController] model validation failures
    /// with the standard ApiResponse envelope.
    /// </summary>
    public static IActionResult CreateInvalidModelStateResponse(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error => MessageFor(entry.Key, error)))
            .Distinct()
            .ToList();

        return new BadRequestObjectResult(ApiResponse.Fail("Dữ liệu không hợp lệ.", errors));
    }

    private static string MessageFor(string key, ModelError error)
    {
        // JSON the serializer could not read ("$.variantId" paths): never echo parser / CLR type details.
        if (error.Exception is not null || key.StartsWith('$'))
        {
            return ValidationMessages.MalformedBody;
        }

        return string.IsNullOrEmpty(error.ErrorMessage) ? ValidationMessages.Invalid : error.ErrorMessage;
    }
}
