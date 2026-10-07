using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Media;
using FurnitureStore.Application.Common.Settings;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FurnitureStore.Infrastructure.Storage;

/// <summary>The stored copies of one upload, largest first.</summary>
public sealed record ProcessedImage(string Extension, string ContentType, IReadOnlyList<ProcessedImageSize> Sizes);

public sealed record ProcessedImageSize(int Width, int Height, byte[] Content);

public interface IImageProcessor
{
    /// <summary>Turns an uploaded picture into the copies described by <paramref name="preset"/>.</summary>
    /// <exception cref="AppValidationException">Not a readable picture, or more pixels than allowed.</exception>
    Task<ProcessedImage> ProcessAsync(byte[] content, ImagePreset preset, CancellationToken cancellationToken = default);
}

/// <summary>
/// Image resizing with ImageSharp (fully managed: no native library, same result on Windows and Linux).
/// Upright from EXIF orientation, scaled down only (Lanczos3, so edges stay crisp), proportions kept; EXIF / IPTC / XMP
/// metadata (camera, GPS position) removed. Opaque pictures are saved as JPEG, pictures with transparency as WebP.
/// </summary>
public sealed class ImageSharpProcessor(IOptions<StorageSettings> options) : IImageProcessor
{
    public const int JpegQuality = 85;
    public const int WebpQuality = 90;

    // Decoding needs ~4 bytes per pixel: a few pictures at a time keeps memory bounded however many are uploaded at once.
    private static readonly SemaphoreSlim Gate = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));

    public async Task<ProcessedImage> ProcessAsync(byte[] content, ImagePreset preset, CancellationToken cancellationToken = default)
    {
        IImageInfo? info;
        try
        {
            info = Image.Identify(content);
        }
        catch (Exception ex) when (ex is ImageFormatException or NotSupportedException)
        {
            info = null;
        }

        if (info is null || info.Width <= 0 || info.Height <= 0)
        {
            throw new AppValidationException("Nội dung tệp không phải là ảnh hợp lệ.");
        }

        var maxMegapixels = options.Value.MaxImageMegapixels;
        if ((long)info.Width * info.Height > maxMegapixels * 1_000_000L)
        {
            throw new AppValidationException($"Ảnh quá lớn ({info.Width} × {info.Height} px). Tối đa {maxMegapixels} megapixel.");
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Process(content, preset), cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static ProcessedImage Process(byte[] content, ImagePreset preset)
    {
        Image<Rgba32> image;
        try
        {
            image = Image.Load<Rgba32>(content);
        }
        catch (Exception ex) when (ex is ImageFormatException or NotSupportedException)
        {
            throw new AppValidationException("Ảnh bị lỗi hoặc không đọc được. Vui lòng chọn ảnh khác.");
        }

        using (image)
        {
            while (image.Frames.Count > 1)
            {
                image.Frames.RemoveFrame(1); // animated WebP: first frame only
            }

            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            if (preset.Fit == ImageFit.Cover)
            {
                var crop = CoverCrop(image.Width, image.Height, preset.MaxWidth, preset.MaxHeight);
                if (crop.Width != image.Width || crop.Height != image.Height)
                {
                    image.Mutate(x => x.Crop(crop));
                }
            }

            var (width, height) = FitInside(image.Width, image.Height, preset.MaxWidth, preset.MaxHeight);
            ResizeTo(image, width, height);

            var transparent = HasTransparency(image);
            IImageEncoder encoder = transparent
                ? new WebpEncoder { Quality = WebpQuality, FileFormat = WebpFileFormatType.Lossy }
                : new JpegEncoder { Quality = JpegQuality };

            var sizes = new List<ProcessedImageSize> { new(image.Width, image.Height, Encode(image, encoder)) };
            foreach (var smaller in preset.SmallerWidths.Where(w => w < image.Width).OrderDescending())
            {
                using var copy = image.Clone();
                ResizeTo(copy, smaller, Math.Max(1, (int)Math.Round(image.Height * (double)smaller / image.Width)));
                sizes.Add(new ProcessedImageSize(copy.Width, copy.Height, Encode(copy, encoder)));
            }

            return transparent
                ? new ProcessedImage(".webp", "image/webp", sizes)
                : new ProcessedImage(".jpg", "image/jpeg", sizes);
        }
    }

    /// <summary>The largest size within the box keeping the proportions; never larger than the picture itself.</summary>
    internal static (int Width, int Height) FitInside(int width, int height, int maxWidth, int maxHeight)
    {
        var scale = Math.Min(1d, Math.Min(maxWidth / (double)width, maxHeight / (double)height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>Centre crop with the box's proportions.</summary>
    internal static Rectangle CoverCrop(int width, int height, int boxWidth, int boxHeight)
    {
        var boxRatio = boxWidth / (double)boxHeight;
        if (width / (double)height > boxRatio)
        {
            var cropWidth = Math.Max(1, (int)Math.Round(height * boxRatio));
            return new Rectangle((width - cropWidth) / 2, 0, cropWidth, height);
        }

        var cropHeight = Math.Max(1, (int)Math.Round(width / boxRatio));
        return new Rectangle(0, (height - cropHeight) / 2, width, cropHeight);
    }

    private static void ResizeTo(Image image, int width, int height)
    {
        if (width < image.Width || height < image.Height)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(width, height),
                Mode = ResizeMode.Stretch, // the size already keeps the proportions
                Sampler = KnownResamplers.Lanczos3
            }));
        }
    }

    private static bool HasTransparency(Image<Rgba32> image)
    {
        var transparent = false;
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height && !transparent; y++)
            {
                foreach (var pixel in rows.GetRowSpan(y))
                {
                    if (pixel.A < byte.MaxValue)
                    {
                        transparent = true;
                        break;
                    }
                }
            }
        });
        return transparent;
    }

    private static byte[] Encode(Image image, IImageEncoder encoder)
    {
        using var output = new MemoryStream();
        image.Save(output, encoder);
        return output.ToArray();
    }
}
