using System.Text.RegularExpressions;
using FluentValidation;
using FurnitureStore.Application.Common.Exceptions;

namespace FurnitureStore.Application.Common.Validation;

public static partial class ValidationExtensions
{
    /// <summary>Validates and throws <see cref="AppValidationException"/> with field-level errors when invalid.</summary>
    public static async Task EnsureValidAsync<T>(this IValidator<T> validator, T instance, CancellationToken cancellationToken = default)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => Describe(e.PropertyName, e.ErrorMessage)).Distinct().ToArray());

        throw new AppValidationException(errors);
    }

    /// <summary>"Variants[1].Price" + "Giá phải lớn hơn 0." → "Biến thể 2: Giá phải lớn hơn 0."</summary>
    private static string Describe(string propertyName, string message)
    {
        var match = VariantIndexRegex().Match(propertyName);
        return match.Success ? $"Biến thể {int.Parse(match.Groups[1].Value) + 1}: {message}" : message;
    }

    [GeneratedRegex(@"^Variants\[(\d+)\]")]
    private static partial Regex VariantIndexRegex();
}
