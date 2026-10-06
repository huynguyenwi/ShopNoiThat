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

    [Range(1, 50)]
    public int MaxFileSizeMb { get; set; } = 5;

    [MinLength(1)]
    public string[] AllowedImageExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".webp"];

    public long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;
}
