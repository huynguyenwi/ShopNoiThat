using System.Globalization;
using System.Text;

namespace FurnitureStore.Application.Common.Utilities;

/// <summary>
/// Builds SEO-friendly URL slugs from Vietnamese text:
/// "Bàn ăn gỗ óc chó 1m8" → "ban-an-go-oc-cho-1m8".
/// </summary>
public static class SlugGenerator
{
    public const int DefaultMaxLength = 200;

    public static string Generate(string? text, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = RemoveDiacritics(text.Trim().ToLowerInvariant());
        var builder = new StringBuilder(normalized.Length);
        var previousDash = false;

        foreach (var c in normalized)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                previousDash = false;
            }
            else if (!previousDash && builder.Length > 0)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > maxLength)
        {
            slug = slug[..maxLength].TrimEnd('-');
        }

        return slug;
    }

    /// <summary>Removes Vietnamese tone marks and converts đ/Đ to d/D.</summary>
    public static string RemoveDiacritics(string text)
    {
        var decomposed = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
