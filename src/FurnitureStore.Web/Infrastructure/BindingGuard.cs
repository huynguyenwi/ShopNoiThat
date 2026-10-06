using FurnitureStore.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FurnitureStore.Web.Infrastructure;

public static class BindingGuard
{
    public const string Summary = "Một số ô nhập có giá trị không hợp lệ (ví dụ chữ trong ô số hoặc ô số bị để trống). Vui lòng kiểm tra lại.";

    /// <summary>
    /// Model binding rejected a posted value ("abc" or "" for a number) and left the property at its default value.
    /// Application commands are validated by FluentValidation, which cannot see that, so form actions call this first:
    /// the action's AppValidationException handler then shows the form again (the field errors are already in
    /// ModelState) instead of saving the default value.
    /// </summary>
    public static void ThrowIfBindingFailed(this ModelStateDictionary modelState)
    {
        if (!modelState.IsValid)
        {
            throw new AppValidationException(Summary);
        }
    }
}
