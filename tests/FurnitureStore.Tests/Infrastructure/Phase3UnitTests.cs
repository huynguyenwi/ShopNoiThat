using FurnitureStore.Application.Common.Emails;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Media;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Infrastructure.Services;
using FurnitureStore.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FurnitureStore.Tests.Infrastructure;

public sealed class ImageFileValidatorTests
{
    private static readonly string[] Allowed = [".jpg", ".jpeg", ".png", ".webp"];
    internal static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static readonly byte[] WebpHeader = "RIFF\0\0\0\0WEBP"u8.ToArray();

    [Theory]
    [InlineData("anh.png")]
    [InlineData("ANH.PNG")]
    public void ValidPng_IsAccepted(string fileName)
    {
        Assert.Empty(ImageFileValidator.Validate(fileName, 2048, PngHeader, Allowed, 5_000_000));
    }

    [Fact]
    public void ValidJpegAndWebp_AreAccepted()
    {
        Assert.Empty(ImageFileValidator.Validate("a.jpg", 100, JpegHeader, Allowed, 5_000_000));
        Assert.Empty(ImageFileValidator.Validate("a.webp", 100, WebpHeader, Allowed, 5_000_000));
    }

    [Fact]
    public void ScriptRenamedToJpg_IsRejectedBySignature()
    {
        var header = "<script>alert"u8.ToArray();

        var errors = ImageFileValidator.Validate("evil.jpg", 100, header, Allowed, 5_000_000);

        Assert.Contains(errors, e => e.Contains("không phải là ảnh"));
    }

    [Theory]
    [InlineData("vector.svg")]
    [InlineData("page.html")]
    [InlineData("shell.php")]
    [InlineData("noextension")]
    [InlineData("anh.png.exe")]
    public void DisallowedExtensions_AreRejected(string fileName)
    {
        Assert.NotEmpty(ImageFileValidator.Validate(fileName, 100, PngHeader, Allowed, 5_000_000));
    }

    [Fact]
    public void OversizedFile_IsRejected()
    {
        var errors = ImageFileValidator.Validate("big.png", 6 * 1024 * 1024, PngHeader, Allowed, 5 * 1024 * 1024);

        Assert.Contains(errors, e => e.Contains("dung lượng"));
    }

    [Theory]
    [InlineData("0912345678", true)]
    [InlineData("+84912345678", true)]
    [InlineData("0312345678", true)]
    [InlineData("0212345678", false)]
    [InlineData("091234567", false)]
    [InlineData("09123456789", false)]
    public void VietnamesePhonePattern(string phone, bool valid)
    {
        Assert.Equal(valid, System.Text.RegularExpressions.Regex.IsMatch(phone, ValidationPatterns.VietnamesePhone));
    }
}

public sealed class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "furniturestore-storage-tests", Guid.NewGuid().ToString("N"));
    private readonly StorageSettings _settings = new();
    private readonly LocalFileStorageService _storage;

    public LocalFileStorageServiceTests()
    {
        Directory.CreateDirectory(_webRoot);
        var environment = new TestWebHostEnvironment { WebRootPath = _webRoot, ContentRootPath = _webRoot };
        var options = Options.Create(_settings);
        _storage = new LocalFileStorageService(environment, options, new ImageSharpProcessor(options), TimeProvider.System,
            NullLogger<LocalFileStorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }

    private string PathOf(string url) => Path.Combine(_webRoot, url.TrimStart('/'));

    private Image<Rgba32> Load(string url) => Image.Load<Rgba32>(PathOf(url));

    [Fact]
    public async Task SaveImage_UsesServerGeneratedName_AndDeleteRemovesEverySize()
    {
        var stored = await _storage.SaveImageAsync(new MemoryStream(TestImages.Jpeg(2000, 1500)), "../../../etc/passwd.jpg", ImagePreset.Product);

        Assert.StartsWith("/uploads/products/", stored.Url);
        Assert.DoesNotContain("passwd", stored.Url);
        Assert.EndsWith("-1200w.jpg", stored.Url);
        Assert.Equal("image/jpeg", stored.ContentType);
        var small = ImageSizes.Small(stored.Url)!;
        Assert.True(File.Exists(PathOf(stored.Url)) && File.Exists(PathOf(small)));

        await _storage.DeleteAsync(stored.Url);
        Assert.False(File.Exists(PathOf(stored.Url)));
        Assert.False(File.Exists(PathOf(small)));
    }

    [Fact]
    public async Task LargePhoto_IsScaledDownToItsFrame_KeepingProportions_WithASmallCopyForCards()
    {
        var stored = await _storage.SaveImageAsync(new MemoryStream(TestImages.Jpeg(4000, 3000)), "phone.jpg", ImagePreset.Product);

        using (var large = Load(stored.Url))
        {
            Assert.Equal((1200, 900), (large.Width, large.Height));
            Assert.True(TestImages.IsClose(TestImages.PixelAt(large, .1, .1), TestImages.TopLeft));
            Assert.True(TestImages.IsClose(TestImages.PixelAt(large, .9, .9), TestImages.BottomRight));
        }

        using (var card = Load(ImageSizes.Small(stored.Url)!))
        {
            Assert.Equal((480, 360), (card.Width, card.Height));
        }

        Assert.True(stored.SizeBytes < 400_000, $"{stored.SizeBytes} bytes kept");
    }

    [Fact]
    public async Task SmallPicture_IsNeverEnlarged()
    {
        var stored = await _storage.SaveImageAsync(new MemoryStream(TestImages.Png(300, 200)), "small.png", ImagePreset.Product);

        Assert.EndsWith("-300w.jpg", stored.Url);
        using var image = Load(stored.Url);
        Assert.Equal((300, 200), (image.Width, image.Height));
        Assert.DoesNotContain(ImageSizes.Of(stored.Url), s => s.Width != 300);   // no "480" copy wider than the picture
    }

    [Fact]
    public async Task Avatar_IsCroppedToASquareFromTheCentre()
    {
        var stored = await _storage.SaveImageAsync(new MemoryStream(TestImages.Jpeg(2000, 1000)), "me.jpg", ImagePreset.Avatar);

        using var image = Load(stored.Url);
        Assert.Equal((256, 256), (image.Width, image.Height));
        Assert.True(TestImages.IsClose(TestImages.PixelAt(image, .1, .1), TestImages.TopLeft));     // centre crop keeps the quarters
        Assert.True(TestImages.IsClose(TestImages.PixelAt(image, .9, .1), TestImages.TopRight));
    }

    [Theory]
    [InlineData(6, 1000, 2000)]   // phone held upright: stored sideways, shown turned 90° clockwise
    [InlineData(8, 1000, 2000)]
    [InlineData(3, 2000, 1000)]
    public async Task PhotoFromAPhone_IsTurnedUpright_FromItsExifOrientation(ushort orientation, int width, int height)
    {
        var stored = await _storage.SaveImageAsync(new MemoryStream(TestImages.Jpeg(2000, 1000, orientation)), "phone.jpg",
            ImagePreset.Product with { MaxWidth = 4000, MaxHeight = 4000 });

        using var image = Load(stored.Url);
        Assert.Equal((width, height), (image.Width, image.Height));
        var expectedTopLeft = orientation switch
        {
            6 => TestImages.BottomLeft,    // turned clockwise: the left column moves to the top
            8 => TestImages.TopRight,
            _ => TestImages.BottomRight    // 3: upside down
        };
        Assert.True(TestImages.IsClose(TestImages.PixelAt(image, .05, .05), expectedTopLeft), TestImages.PixelAt(image, .05, .05).ToString());
        Assert.Null(image.Metadata.ExifProfile);                     // upright pixels, no orientation left to apply
    }

    [Fact]
    public async Task CameraMetadata_AndGpsPosition_AreRemoved()
    {
        var stored = await _storage.SaveImageAsync(new MemoryStream(TestImages.Jpeg(800, 600, withGps: true)), "trip.jpg", ImagePreset.Review);

        using var image = Load(stored.Url);
        Assert.Null(image.Metadata.ExifProfile);
    }

    [Fact]
    public async Task TransparentPicture_KeepsItsTransparency_AsWebp()
    {
        var stored = await _storage.SaveImageAsync(new MemoryStream(TestImages.Png(400, 400, transparent: true)), "logo.png", ImagePreset.Product);

        Assert.EndsWith(".webp", stored.Url);
        Assert.Equal("image/webp", stored.ContentType);
        using var image = Load(stored.Url);
        Assert.True(TestImages.PixelAt(image, .05, .05).A < 50);
    }

    [Fact]
    public async Task TooManyPixels_AreRefusedBeforeDecoding()
    {
        _settings.MaxImageMegapixels = 1;

        var error = await Assert.ThrowsAsync<AppValidationException>(() =>
            _storage.SaveImageAsync(new MemoryStream(TestImages.Png(2000, 1000)), "huge.png", ImagePreset.Product));

        Assert.Contains("2000 × 1000", error.Message + string.Join(" ", error.Errors));
        Assert.False(Directory.Exists(Path.Combine(_webRoot, "uploads")));
    }

    [Fact]
    public async Task FileOverTheSizeLimit_IsRefused()
    {
        _settings.MaxFileSizeMb = 1;
        var big = TestImages.Png(1200, 900).Concat(new byte[1_100_000]).ToArray();

        var error = await Assert.ThrowsAsync<AppValidationException>(() => _storage.SaveImageAsync(new MemoryStream(big), "big.png", ImagePreset.Product));

        Assert.Contains("1 MB", string.Join(" ", error.Errors));
    }

    [Fact]
    public async Task SaveImage_WithFakeImage_ThrowsValidation_AndWritesNothing()
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _storage.SaveImageAsync(new MemoryStream("not an image at all"u8.ToArray()), "fake.png", ImagePreset.Avatar));
        // Right signature, broken content.
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _storage.SaveImageAsync(new MemoryStream(ImageFileValidatorTests.PngHeader.Concat(new byte[100]).ToArray()), "broken.png", ImagePreset.Avatar));

        Assert.False(Directory.Exists(Path.Combine(_webRoot, "uploads")));
    }

    [Fact]
    public async Task SaveImage_RejectsUnsafeFolderName()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.SaveImageAsync(new MemoryStream(TestImages.Png(10, 10)), "a.png", ImagePreset.Avatar with { Folder = "../outside" }));
    }

    [Fact]
    public async Task Delete_RefusesPathsOutsideUploads()
    {
        var outside = Path.Combine(_webRoot, "keep.txt");
        await File.WriteAllTextAsync(outside, "important");

        await _storage.DeleteAsync("/uploads/../keep.txt");
        await _storage.DeleteAsync("/keep.txt");

        Assert.True(File.Exists(outside));
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
    }
}

public sealed class ImageSizesTests
{
    [Fact]
    public void ResizedProductPicture_HasItsSmallCopy_InSrcSet()
    {
        const string url = "/uploads/products/2026/10/ab12-1200w.jpg";

        Assert.Equal("/uploads/products/2026/10/ab12-480w.jpg 480w, /uploads/products/2026/10/ab12-1200w.jpg 1200w", ImageSizes.SrcSet(url));
        Assert.Equal("/uploads/products/2026/10/ab12-480w.jpg", ImageSizes.Small(url));
        Assert.Equal(url, ImageSizes.Small(url, 600));          // a 600 px slot needs the large one
    }

    [Theory]
    [InlineData("/uploads/products/2026/09/0f3c.jpg")]                  // uploaded before resizing existed
    [InlineData("/placeholder/product.svg?shape=chair&color=5C4033")]   // seeded placeholder
    [InlineData("/uploads/avatars/2026/10/ab12-256w.jpg")]              // avatars have one size
    [InlineData(null)]
    public void PicturesWithASingleSize_AreUsedAsTheyAre(string? url)
    {
        Assert.Null(ImageSizes.SrcSet(url));
        Assert.Equal(url, ImageSizes.Small(url));
    }

    [Fact]
    public void NarrowPicture_HasNoCopyWiderThanItself()
    {
        Assert.Null(ImageSizes.SrcSet("/uploads/products/2026/10/ab12-300w.jpg"));
    }
}

public sealed class AuditAndEmailTests
{
    [Fact]
    public void AuditSerialization_RemovesSensitiveFieldsRecursively()
    {
        var json = AuditLogService.Serialize(new
        {
            Email = "a@b.c",
            PasswordHash = "AQAAAA...",
            Nested = new { ApiKey = "sk-123", SecurityStamp = "x", Name = "Bàn ăn" },
            Items = new[] { new { reset_token = "t", Qty = 1 } }
        })!;

        Assert.Contains("a@b.c", json);
        Assert.Contains("Bàn ăn", json);
        Assert.DoesNotContain("AQAAAA", json);
        Assert.DoesNotContain("sk-123", json);
        Assert.DoesNotContain("SecurityStamp", json);
        Assert.DoesNotContain("reset_token", json);
        Assert.Contains("\"Qty\":1", json);
    }

    [Fact]
    public void EmailTemplates_EncodeUserInput()
    {
        var html = EmailTemplates.Welcome("Nhà Mộc", "<script>alert(1)</script>", "https://shop/products?a=1&b=2");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("https://shop/products?a=1&amp;b=2", html);
    }
}
