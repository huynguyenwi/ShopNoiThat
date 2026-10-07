namespace FurnitureStore.Application.Common.Validation;

/// <summary>
/// Validates uploaded images by extension, size and file signature ("magic bytes"), so a script renamed
/// to .jpg is rejected. SVG is intentionally not allowed because it can carry JavaScript.
/// </summary>
public static class ImageFileValidator
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".jfif"] = "image/jpeg", // what Windows names JPEGs saved from a browser
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    public const int SignatureLength = 12;

    public static string? ContentTypeFor(string extension) => ContentTypes.GetValueOrDefault(extension);

    /// <summary>Returns the list of problems; empty when the file is acceptable.</summary>
    public static IReadOnlyList<string> Validate(string? fileName, long length, ReadOnlySpan<byte> header,
        IReadOnlyCollection<string> allowedExtensions, long maxBytes)
    {
        var errors = new List<string>();
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

        if (string.IsNullOrEmpty(extension)
            || !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || !ContentTypes.ContainsKey(extension))
        {
            errors.Add($"Định dạng ảnh không được hỗ trợ. Chỉ chấp nhận: {string.Join(", ", allowedExtensions)}.");
            return errors;
        }

        if (length <= 0)
        {
            errors.Add("Tệp ảnh rỗng.");
            return errors;
        }

        if (length > maxBytes)
        {
            errors.Add($"Ảnh vượt quá dung lượng cho phép ({maxBytes / (1024 * 1024)} MB).");
        }

        if (!SignatureMatches(extension, header))
        {
            errors.Add("Nội dung tệp không phải là ảnh hợp lệ.");
        }

        return errors;
    }

    public static bool SignatureMatches(string extension, ReadOnlySpan<byte> header) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jfif" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        ".png" => header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        // "RIFF" <size> "WEBP"
        ".webp" => header.Length >= 12
                   && header[..4].SequenceEqual("RIFF"u8)
                   && header.Slice(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };
}
