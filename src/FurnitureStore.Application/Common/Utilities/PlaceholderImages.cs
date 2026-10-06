using System.Text.RegularExpressions;

namespace FurnitureStore.Application.Common.Utilities;

/// <summary>
/// Copyright-free product illustrations for the MVP. Each URL points to an SVG rendered by the site
/// (/images/placeholder/{shape}.svg) and colored with the variant's color, e.g.
/// /images/placeholder/sofa.svg?color=6b7f59&amp;bg=f1e9dd. Real photos uploaded by admins replace them.
/// </summary>
public static partial class PlaceholderImages
{
    public const string BasePath = "/images/placeholder/";

    public static readonly IReadOnlySet<string> Shapes = new HashSet<string>(StringComparer.Ordinal)
    {
        "sofa", "armchair", "coffee-table", "tv-stand", "bed", "wardrobe", "nightstand", "vanity",
        "dining-table", "chair", "dining-set", "cabinet", "desk", "office-chair", "bookshelf", "wall-shelf", "mirror"
    };

    /// <summary>Background tones used for gallery images.</summary>
    public static class Backgrounds
    {
        public const string Cream = "faf7f2";
        public const string Beige = "f1e9dd";
        public const string Sand = "e3d5c1";
        public const string White = "ffffff";
    }

    public static string BuildUrl(string shape, string colorHex, string backgroundHex = Backgrounds.Beige)
    {
        if (!Shapes.Contains(shape))
        {
            throw new ArgumentException($"Unknown placeholder shape '{shape}'.", nameof(shape));
        }

        return $"{BasePath}{shape}.svg?color={NormalizeHex(colorHex)}&bg={NormalizeHex(backgroundHex)}";
    }

    /// <summary>"#5C4033" → "5c4033". Throws for anything that is not a 6-digit hex color.</summary>
    public static string NormalizeHex(string hex)
    {
        var value = hex.TrimStart('#').ToLowerInvariant();
        if (!HexColorRegex().IsMatch(value))
        {
            throw new ArgumentException($"'{hex}' is not a 6-digit hex color.", nameof(hex));
        }

        return value;
    }

    public static bool IsValidHex(string? value) => value is not null && HexColorRegex().IsMatch(value);

    [GeneratedRegex("^[0-9a-f]{6}$")]
    private static partial Regex HexColorRegex();
}
