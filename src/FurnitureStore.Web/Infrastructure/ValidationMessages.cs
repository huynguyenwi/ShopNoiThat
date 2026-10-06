using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Localization;

namespace FurnitureStore.Web.Infrastructure;

/// <summary>
/// Vietnamese replacements for the framework's built-in English validation texts ("The X field is required.",
/// "The field X must be a number.", "The value 'abc' is not valid."). Business rules have their own messages
/// (FluentValidation / explicit ErrorMessage); these cover model binding and attributes declared without a message,
/// including the implicit [Required] MVC adds for non-nullable value types.
/// </summary>
public static class ValidationMessages
{
    public const string Required = "Vui lòng nhập giá trị.";
    public const string Invalid = "Giá trị không hợp lệ.";
    public const string MustBeNumber = "Vui lòng nhập một số hợp lệ.";
    public const string MalformedBody = "Dữ liệu gửi lên không đúng định dạng.";

    public static void Configure(DefaultModelBindingMessageProvider provider)
    {
        provider.SetMissingBindRequiredValueAccessor(_ => Required);
        provider.SetMissingKeyOrValueAccessor(() => Required);
        provider.SetMissingRequestBodyRequiredValueAccessor(() => MalformedBody);
        provider.SetValueMustNotBeNullAccessor(_ => Required);
        provider.SetAttemptedValueIsInvalidAccessor((_, _) => Invalid);
        provider.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => Invalid);
        provider.SetUnknownValueIsInvalidAccessor(_ => Invalid);
        provider.SetNonPropertyUnknownValueIsInvalidAccessor(() => Invalid);
        provider.SetValueIsInvalidAccessor(_ => Invalid);
        provider.SetValueMustBeANumberAccessor(_ => MustBeNumber);
        provider.SetNonPropertyValueMustBeANumberAccessor(() => MustBeNumber);
    }

    /// <summary>Gives an attribute declared without a message a Vietnamese one ({1}, {2} are the attribute's limits).</summary>
    public static void ApplyDefault(ValidationAttribute attribute)
    {
        if (!string.IsNullOrEmpty(attribute.ErrorMessage) || !string.IsNullOrEmpty(attribute.ErrorMessageResourceName))
        {
            return;
        }

        attribute.ErrorMessage = attribute switch
        {
            RequiredAttribute => Required,
            RangeAttribute => "Giá trị phải từ {1} đến {2}.",
            StringLengthAttribute { MinimumLength: > 0 } => "Độ dài phải từ {2} đến {1} ký tự.",
            StringLengthAttribute or MaxLengthAttribute => "Tối đa {1} ký tự.",
            MinLengthAttribute => "Tối thiểu {1} ký tự.",
            EmailAddressAttribute => "Email không hợp lệ.",
            PhoneAttribute => "Số điện thoại không hợp lệ.",
            UrlAttribute => "Đường dẫn không hợp lệ.",
            CompareAttribute => "Giá trị nhập lại không khớp.",
            _ => Invalid
        };
    }
}

/// <summary>Server-side validation: attributes found on models.</summary>
public sealed class DefaultValidationMessagesProvider : IValidationMetadataProvider
{
    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        foreach (var attribute in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
        {
            ValidationMessages.ApplyDefault(attribute);
        }
    }
}

/// <summary>
/// Client-side validation (data-val-* attributes). MVC creates the implicit [Required] of value types right here,
/// so it never goes through <see cref="DefaultValidationMessagesProvider"/>.
/// </summary>
public sealed class LocalizedValidationAttributeAdapterProvider : IValidationAttributeAdapterProvider
{
    private readonly ValidationAttributeAdapterProvider inner = new();

    public IAttributeAdapter? GetAttributeAdapter(ValidationAttribute attribute, IStringLocalizer? stringLocalizer)
    {
        ValidationMessages.ApplyDefault(attribute);
        return inner.GetAttributeAdapter(attribute, stringLocalizer);
    }
}
