using System.Text.RegularExpressions;

namespace FurnitureStore.Application.Common.Media;

public enum ImageFit
{
    /// <summary>The whole picture, scaled down to fit inside the box (proportions kept, nothing cut).</summary>
    Contain,

    /// <summary>Centre crop to the box's proportions, then scaled down (round avatars).</summary>
    Cover
}

/// <summary>
/// How an uploaded picture is prepared for where it is shown. Pictures are only ever scaled <b>down</b> (never up, so
/// they never turn blurry or blocky), keep their proportions, are turned upright from the camera's EXIF orientation and
/// lose their metadata (e.g. GPS position). Smaller copies are stored next to the largest one for lists and cards.
/// </summary>
/// <param name="Folder">Folder under the uploads root.</param>
/// <param name="SmallerWidths">Extra widths stored for small frames (when narrower than the largest copy).</param>
public sealed record ImagePreset(string Folder, int MaxWidth, int MaxHeight, ImageFit Fit, IReadOnlyList<int> SmallerWidths)
{
    /// <summary>Product gallery (shown ~650 px wide, 1200 for sharp high-DPI screens); 480 px copy for product cards.</summary>
    public static readonly ImagePreset Product = new("products", 1200, 1200, ImageFit.Contain, [480]);

    /// <summary>Review photos (opened full size); 320 px copy for the thumbnails under a review.</summary>
    public static readonly ImagePreset Review = new("reviews", 1280, 1280, ImageFit.Contain, [320]);

    /// <summary>Avatars: square, shown at most 88 px wide (256 covers 3x screens).</summary>
    public static readonly ImagePreset Avatar = new("avatars", 256, 256, ImageFit.Cover, []);

    public static readonly IReadOnlyList<ImagePreset> All = [Product, Review, Avatar];
}

/// <summary>
/// URLs of the sizes stored for an uploaded picture. Files are named "{id}-{width}w.{ext}": the URL saved in the database
/// is the largest copy, e.g. "/uploads/products/2026/10/ab12-1200w.jpg", and "ab12-480w.jpg" sits next to it.
/// Pictures uploaded before this naming (or seeded placeholders) have a single size and are returned unchanged.
/// </summary>
public static partial class ImageSizes
{
    /// <summary>All stored widths of a picture, smallest first; empty when it has a single size.</summary>
    public static IReadOnlyList<(int Width, string Url)> Of(string? url)
    {
        if (string.IsNullOrEmpty(url) || SizedName().Match(url) is not { Success: true } match)
        {
            return [];
        }

        var largest = int.Parse(match.Groups["width"].Value);
        var preset = ImagePreset.All.FirstOrDefault(p => url.Contains("/" + p.Folder + "/", StringComparison.Ordinal));
        var widths = (preset?.SmallerWidths ?? []).Where(w => w < largest).Append(largest).Order();
        return widths.Select(w => (w, $"{match.Groups["stem"].Value}-{w}w{match.Groups["ext"].Value}")).ToList();
    }

    /// <summary>"…-480w.jpg 480w, …-1200w.jpg 1200w" for an img srcset, or null for a single-size picture.</summary>
    public static string? SrcSet(string? url)
    {
        var sizes = Of(url);
        return sizes.Count < 2 ? null : string.Join(", ", sizes.Select(s => $"{s.Url} {s.Width}w"));
    }

    /// <summary>The smallest stored copy at least <paramref name="minWidth"/> wide (for tiny thumbnails), else the picture itself.</summary>
    public static string? Small(string? url, int minWidth = 0)
    {
        var sizes = Of(url);
        return sizes.Count == 0 ? url : sizes.FirstOrDefault(s => s.Width >= minWidth, sizes[^1]).Url;
    }

    [GeneratedRegex(@"^(?<stem>.+)-(?<width>\d{2,5})w(?<ext>\.(?:jpg|webp))$")]
    private static partial Regex SizedName();
}
