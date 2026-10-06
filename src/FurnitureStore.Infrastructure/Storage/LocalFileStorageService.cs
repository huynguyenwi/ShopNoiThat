using System.Text.RegularExpressions;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Common.Validation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Infrastructure.Storage;

/// <summary>
/// Stores uploads under wwwroot/{LocalRootFolder}/{folder}/{yyyy}/{MM}/{random}.{ext} and serves them as static files.
/// File names are generated server-side, so client-provided names can never cause path traversal or overwrite files.
/// </summary>
public sealed partial class LocalFileStorageService(
    IWebHostEnvironment environment,
    IOptions<StorageSettings> options,
    TimeProvider timeProvider,
    ILogger<LocalFileStorageService> logger) : IFileStorageService
{
    public async Task<StoredFile> SaveImageAsync(Stream content, string originalFileName, string folder, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!SafeFolderRegex().IsMatch(folder))
        {
            throw new ArgumentException("Folder may only contain lowercase letters, digits and dashes.", nameof(folder));
        }

        // Buffer the upload (bounded by the size limit) so we can inspect the signature before writing to disk.
        await using var buffer = new MemoryStream();
        var limit = settings.MaxFileSizeBytes + 1;
        var copyBuffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(copyBuffer, cancellationToken)) > 0)
        {
            buffer.Write(copyBuffer, 0, read);
            if (buffer.Length > limit)
            {
                break;
            }
        }

        var errors = Validate(buffer, originalFileName, settings);
        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var now = timeProvider.GetUtcNow();
        var relativeDirectory = Path.Combine(settings.LocalRootFolder, folder, now.ToString("yyyy"), now.ToString("MM"));
        var absoluteDirectory = Path.Combine(WebRoot, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(absoluteDirectory, fileName);

        buffer.Position = 0;
        await using (var file = new FileStream(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await buffer.CopyToAsync(file, cancellationToken);
        }

        var url = "/" + Path.Combine(relativeDirectory, fileName).Replace('\\', '/');
        logger.LogInformation("Stored image {Url} ({Bytes} bytes)", url, buffer.Length);
        return new StoredFile(url, fileName, buffer.Length, ImageFileValidator.ContentTypeFor(extension)!);
    }

    public Task DeleteAsync(string? url, CancellationToken cancellationToken = default)
    {
        var prefix = "/" + options.Value.LocalRootFolder.Trim('/') + "/";
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var uploadsRoot = Path.GetFullPath(Path.Combine(WebRoot, options.Value.LocalRootFolder));
        var fullPath = Path.GetFullPath(Path.Combine(WebRoot, url.TrimStart('/')));

        // Never delete anything outside the uploads folder.
        if (!fullPath.StartsWith(uploadsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Refused to delete file outside uploads folder: {Url}", url);
            return Task.CompletedTask;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            logger.LogInformation("Deleted image {Url}", url);
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyList<string> Validate(MemoryStream buffer, string originalFileName, StorageSettings settings)
    {
        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, ImageFileValidator.SignatureLength));
        return ImageFileValidator.Validate(originalFileName, buffer.Length, header, settings.AllowedImageExtensions, settings.MaxFileSizeBytes);
    }

    private string WebRoot =>environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex SafeFolderRegex();
}
