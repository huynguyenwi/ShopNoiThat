using System.Text;
using FurnitureStore.Application.Common.Utilities;

namespace FurnitureStore.Application.Catalog;

/// <summary>Builds and normalizes the accent-free text used by product search and autocomplete.</summary>
public static class ProductSearchText
{
    public const int MaxLength = 1000;

    public static string Build(string name, string sku, string? categoryName, string? parentCategoryName, string? styleName,
        IEnumerable<string> materialNames, IEnumerable<string> colorNames, string? furnitureTypeName = null)
    {
        var parts = new[] { name, sku, categoryName, parentCategoryName, styleName, furnitureTypeName }
            .Concat(materialNames)
            .Concat(colorNames)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Normalize(p!))
            .Distinct();

        var text = string.Join(' ', parts);
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }

    /// <summary>"Bàn ĂN  gỗ-Óc chó" → "ban an go oc cho".</summary>
    public static string Normalize(string text)
    {
        var noAccents = SlugGenerator.RemoveDiacritics(text.ToLowerInvariant());
        var builder = new StringBuilder(noAccents.Length);
        var lastWasSpace = true;
        foreach (var c in noAccents)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>Splits a user query into normalized terms (max 6) that must all appear in SearchText.</summary>
    public static IReadOnlyList<string> Terms(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().Take(6).ToList();
}
