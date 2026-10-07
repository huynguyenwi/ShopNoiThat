using System.Text.RegularExpressions;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Media;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Common.Validation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Infrastructure.Storage;

/// <summary>
/// Stores uploads under wwwroot/{LocalRootFolder}/{folder}/{yyyy}/{MM}/{random}-{width}w.{ext} and serves them as static
/// files: the picture resized for its preset, plus its smaller copies (see <see cref="ImageSizes"/>).
/// File names are generated server-side, so client-provided names can never cause path traversal or overwrite files.
/// </summary>
public sealed partial class LocalFileStorageService(
    IWebHostEnvironment environment,
    IOptions<StorageSettings> options,
    IImageProcessor imageProcessor,
    TimeProvider timeProvider,
    ILogger<LocalFileStorageService> logger) : IFileStorageService
{
    public async Task<StoredFile> SaveImageAsync(Stream content, string originalFileName, ImagePreset preset, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var folder = preset.Folder;
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

        var processed = await imageProcessor.ProcessAsync(buffer.ToArray(), preset, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var relativeDirectory = Path.Combine(settings.LocalRootFolder, folder, now.ToString("yyyy"), now.ToString("MM"));
        var absoluteDirectory = Path.Combine(WebRoot, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        var id = Guid.NewGuid().ToString("N");
        var written = new List<string>();
        try
        {
            foreach (var size in processed.Sizes)
            {
                var path = Path.Combine(absoluteDirectory, $"{id}-{size.Width}w{processed.Extension}");
                await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                written.Add(path);
                await file.WriteAsync(size.Content, cancellationToken);
            }
        }
        catch
        {
            written.ForEach(File.Delete);
            throw;
        }

        var largest = processed.Sizes[0];
        var fileName = $"{id}-{largest.Width}w{processed.Extension}";
        var url = "/" + Path.Combine(relativeDirectory, fileName).Replace('\\', '/');
        logger.LogInformation("Stored image {Url}: {Width}x{Height}, {Copies} sizes, {UploadedBytes} bytes uploaded, {StoredBytes} bytes kept",
            url, largest.Width, largest.Height, processed.Sizes.Count, buffer.Length, processed.Sizes.Sum(s => (long)s.Content.Length));
        return new StoredFile(url, fileName, largest.Content.Length, processed.ContentType);
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

        // Smaller copies of a resized upload: "{id}-480w.jpg" next to "{id}-1200w.jpg".
        foreach (var (_, copyUrl) in ImageSizes.Of(url).Where(s => s.Url != url))
        {
            var copyPath = Path.GetFullPath(Path.Combine(WebRoot, copyUrl.TrimStart('/')));
            if (copyPath.StartsWith(uploadsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(copyPath))
            {
                File.Delete(copyPath);
            }
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
