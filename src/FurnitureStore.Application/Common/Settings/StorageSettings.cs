using System.ComponentModel.DataAnnotations;

namespace FurnitureStore.Application.Common.Settings;

/// <summary>
/// File upload rules, bound from the "Storage" configuration section.
/// </summary>
public sealed class StorageSettings
{
    public const string SectionName = "Storage";

    /// <summary>"Local" for MVP. Cloud providers (Azure Blob, S3) can be added behind IFileStorageService.</summary>
    [Required]
    public string Provider { get; set; } = "Local";

    /// <summary>Folder under wwwroot where uploaded files are written.</summary>
    [Required]
    public string LocalRootFolder { get; set; } = "uploads";

    /// <summary>
    /// Largest picture accepted, as uploaded (phone and camera photos are 2 - 20 MB). Only the resized copies are kept,
    /// so this only bounds what one request may make the server read. At most <see cref="UploadLimits.MaxImageMb"/>.
    /// </summary>
    [Range(1, UploadLimits.MaxImageMb)]
    public int MaxFileSizeMb { get; set; } = UploadLimits.MaxImageMb;

    /// <summary>Largest picture in pixels (decoding needs ~4 bytes per pixel in memory): 100 MP covers 108 MP phone cameras.</summary>
    [Range(1, 300)]
    public int MaxImageMegapixels { get; set; } = 100;

    [MinLength(1)]
    public string[] AllowedImageExtensions { get; set; } = [".jpg", ".jpeg", ".jfif", ".png", ".webp"];

    public long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;
}

/// <summary>Upper bounds of image uploads, used by the request size limits of the upload endpoints.</summary>
public static class UploadLimits
{
    public const int MaxImageMb = 50;

    /// <summary>Request size for <paramref name="images"/> pictures of the largest size plus the other form fields.</summary>
    public const long PerImageBytes = MaxImageMb * 1024L * 1024L;

    public const long FormFieldsBytes = 2 * 1024L * 1024L;
}
