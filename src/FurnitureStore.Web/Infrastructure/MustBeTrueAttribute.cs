using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace FurnitureStore.Web.Infrastructure;

/// <summary>
/// A checkbox that must be ticked (e.g. "I accept the terms"). Unlike [Range(typeof(bool), "true", "true")], whose client
/// rule jQuery Validate cannot evaluate on a checkbox, this emits data-val-mustbetrue handled in _ValidationScriptsPartial.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MustBeTrueAttribute : ValidationAttribute, IClientModelValidator
{
    public override bool IsValid(object? value) => value is true;

    public void AddValidation(ClientModelValidationContext context)
    {
        context.Attributes.TryAdd("data-val", "true");
        context.Attributes.TryAdd("data-val-mustbetrue", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
    }
}
