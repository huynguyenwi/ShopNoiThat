using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

/// <summary>Opening admin notifications one by one, and picture uploads of any phone-photo size (resized by the server).</summary>
public sealed partial class NotificationsAndUploadsWebTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private Task<HttpClient> AdminAsync() =>
        CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<int> AddAdminNotificationAsync(string title, string? link) => DbAsync(async db =>
    {
        var n = new Notification { RecipientRole = FurnitureStore.Domain.Constants.AppRoles.Admin, Type = NotificationType.NewContactMessage, Title = title, Message = "Nội dung " + title, Link = link, CreatedAt = DateTime.UtcNow };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        return n.Id;
    });

    private static int BellCount(string html) => int.Parse(BellRegex().Match(html).Groups[1].Value);

    // ------------------------------------------------------------------ notifications

    [Fact]
    public async Task OpeningANotification_MarksItRead_LowersTheBell_AndShowsItsPage()
    {
        var first = await AddAdminNotificationAsync("Liên hệ mới A", "/admin/contacts");
        var second = await AddAdminNotificationAsync("Liên hệ mới B", "/admin/contacts");
        var admin = await AdminAsync();

        var page = await admin.GetStringAsync("/admin/notifications");
        var before = BellCount(page);
        Assert.Matches($"class=\"notification-item is-unread\" data-notification=\"{first}\"", page);
        Assert.Contains("notification-new", page);

        var opened = await PostFormAsync(admin, "/admin/notifications", $"/admin/notifications/{first}/open", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Redirect, opened.StatusCode);
        Assert.Equal("/admin/contacts", opened.Headers.Location!.ToString());
        var after = await admin.GetStringAsync("/admin/notifications");
        Assert.Equal(before - 1, BellCount(after));
        Assert.Matches($"class=\"notification-item is-read\" data-notification=\"{first}\"", after);
        Assert.Matches($"class=\"notification-item is-unread\" data-notification=\"{second}\"", after);
    }

    [Fact]
    public async Task ANotificationLink_ToAnotherSite_IsNotFollowed()
    {
        var id = await AddAdminNotificationAsync("Liên kết lạ", "https://evil.example/phish");
        var admin = await AdminAsync();

        var opened = await PostFormAsync(admin, "/admin/notifications", $"/admin/notifications/{id}/open", new Dictionary<string, string>());

        Assert.Equal("/admin/notifications", opened.Headers.Location!.ToString());
        Assert.True(await DbAsync(db => db.Notifications.Where(n => n.Id == id).Select(n => n.IsRead).SingleAsync()));
    }

    [Fact]
    public async Task Customers_CannotOpenAdminNotifications()
    {
        var id = await AddAdminNotificationAsync("Chỉ admin", "/admin/orders");
        var customer = factory.CreateClient();
        await RegisterAsync(customer);
        var token = await GetAntiforgeryTokenAsync(customer, "/account/profile");

        var response = await customer.PostAsync($"/admin/notifications/{id}/open",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("/admin/orders", response.Headers.Location?.ToString() ?? string.Empty);
        Assert.False(await DbAsync(db => db.Notifications.Where(n => n.Id == id).Select(n => n.IsRead).SingleAsync()));
    }

    // ------------------------------------------------------------------ uploads

    /// <summary>A photo as big as a phone's: noise compresses badly, like real photos.</summary>
    private static byte[] PhonePhoto(int width = 4000, int height = 3000)
    {
        using var image = TestImages.Quarters(width, height);
        var random = new Random(7);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                foreach (ref var pixel in rows.GetRowSpan(y))
                {
                    pixel = new Rgba32((byte)Math.Clamp(pixel.R + random.Next(-60, 60), 0, 255), (byte)Math.Clamp(pixel.G + random.Next(-60, 60), 0, 255),
                        (byte)Math.Clamp(pixel.B + random.Next(-60, 60), 0, 255));
                }
            }
        });
        using var output = new MemoryStream();
        image.Save(output, new JpegEncoder { Quality = 95 });
        return output.ToArray();
    }

    private static async Task<HttpResponseMessage> UploadAvatarAsync(HttpClient client, byte[] content, string fileName = "anh-dien-thoai.jpg")
    {
        var token = await GetAntiforgeryTokenAsync(client, "/account/profile");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "avatar", fileName);
        return await client.PostAsync("/account/avatar", form);
    }

    [Fact]
    public async Task AvatarFromAPhonePhoto_OverTenMegabytes_IsResizedAndShown()
    {
        var client = factory.CreateClient();
        var email = await RegisterAsync(client);
        var photo = PhonePhoto();
        Assert.True(photo.Length > 10 * 1024 * 1024, $"{photo.Length} bytes"); // the size that failed before (10 MB request limit)

        var response = await UploadAvatarAsync(client, photo);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var profile = await client.GetStringAsync("/account/profile");
        Assert.Contains("Đã cập nhật ảnh đại diện.", profile);
        var url = await DbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.AvatarUrl).SingleAsync());
        Assert.Matches(@"^/uploads/avatars/\d{4}/\d{2}/[0-9a-f]{32}-256w\.jpg$", url!);
        Assert.Contains($"src=\"{url}\"", profile);                                     // profile picture
        Assert.Contains($"class=\"user-menu-avatar\" src=\"{url}\"", profile);          // header

        var path = Path.Combine(factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath, url!.TrimStart('/'));
        using (var stored = Image.Load<Rgba32>(path))
        {
            Assert.Equal((256, 256), (stored.Width, stored.Height));
        }

        Assert.True(new FileInfo(path).Length < 60_000);
        File.Delete(path);
    }

    [Fact]
    public async Task UploadOverTheLimit_GetsAClearMessage()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);
        var tooBig = TestImages.Png(16, 16).Concat(new byte[53 * 1024 * 1024]).ToArray();

        var response = await UploadAvatarAsync(client, tooBig, "qua-lon.png");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Tệp quá lớn", html);
        Assert.Contains("tối đa 50 MB mỗi ảnh", html);
    }

    [GeneratedRegex("aria-label=\"Thông báo \\((\\d+) chưa đọc\\)\"")]
    private static partial Regex BellRegex();
}
