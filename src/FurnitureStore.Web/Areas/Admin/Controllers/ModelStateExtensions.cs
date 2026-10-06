using FurnitureStore.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FurnitureStore.Web.Areas.Admin.Controllers;

public static class ModelStateExtensions
{
    /// <summary>
    /// Copies validation errors from the application layer into ModelState. Field errors are keyed with
    /// <paramref name="prefix"/> (e.g. "Command.Name") so tag helpers show them next to the input.
    /// </summary>
    public static void AddApplicationErrors(this ModelStateDictionary modelState, AppValidationException exception, string prefix = "Command")
    {
        if (exception.FieldErrors.Count == 0)
        {
            foreach (var error in exception.Errors)
            {
                modelState.AddModelError(string.Empty, error);
            }
            return;
        }

        foreach (var (field, messages) in exception.FieldErrors)
        {
            // Variant rows use custom keys in the form, so their errors go to the summary (messages say "Biến thể n: ...").
            var key = field.StartsWith("Variants[", StringComparison.Ordinal) || string.IsNullOrEmpty(field)
                ? string.Empty
                : string.IsNullOrEmpty(prefix) ? field : $"{prefix}.{field}";
            foreach (var message in messages)
            {
                modelState.AddModelError(key, message);
            }
        }
    }
}
