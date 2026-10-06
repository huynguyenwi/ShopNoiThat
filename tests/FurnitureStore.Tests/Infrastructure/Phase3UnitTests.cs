using FurnitureStore.Application.Common.Emails;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Infrastructure.Services;
using FurnitureStore.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
    private readonly LocalFileStorageService _storage;

    public LocalFileStorageServiceTests()
    {
        Directory.CreateDirectory(_webRoot);
        var environment = new TestWebHostEnvironment { WebRootPath = _webRoot, ContentRootPath = _webRoot };
        _storage = new LocalFileStorageService(environment, Options.Create(new StorageSettings()), TimeProvider.System, NullLogger<LocalFileStorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SaveImage_UsesServerGeneratedName_AndDeleteRemovesIt()
    {
        var bytes = ImageFileValidatorTests.PngHeader.Concat(new byte[100]).ToArray();

        var stored = await _storage.SaveImageAsync(new MemoryStream(bytes), "../../../etc/passwd.png", "avatars");

        Assert.StartsWith("/uploads/avatars/", stored.Url);
        Assert.DoesNotContain("passwd", stored.Url);
        Assert.Equal("image/png", stored.ContentType);
        var path = Path.Combine(_webRoot, stored.Url.TrimStart('/'));
        Assert.True(File.Exists(path));

        await _storage.DeleteAsync(stored.Url);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task SaveImage_WithFakeImage_ThrowsValidation_AndWritesNothing()
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _storage.SaveImageAsync(new MemoryStream("not an image at all"u8.ToArray()), "fake.png", "avatars"));

        Assert.False(Directory.Exists(Path.Combine(_webRoot, "uploads")));
    }

    [Fact]
    public async Task SaveImage_RejectsUnsafeFolderName()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.SaveImageAsync(new MemoryStream(ImageFileValidatorTests.PngHeader), "a.png", "../outside"));
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
