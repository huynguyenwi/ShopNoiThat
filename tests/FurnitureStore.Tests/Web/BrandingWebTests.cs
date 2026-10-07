using System.Net;
using System.Net.Http.Headers;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

/// <summary>The store name from /admin/store everywhere (logo, titles, footer), and the home page banner edited in /admin/banner.</summary>
public sealed class BrandingWebTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private Task<HttpClient> AdminAsync() =>
        CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static Dictionary<string, string> StoreFields(string name, string subtitle) => new()
    {
        ["Name"] = name, ["LogoSubtitle"] = subtitle, ["Tagline"] = "Bàn ghế ăn gỗ sồi Nga - Đóng tại xưởng",
        ["Address"] = "123 Đường Nguyễn Văn Linh, TP. Hồ Chí Minh", ["Hotline"] = "1900 0000", ["Email"] = "contact@furniture.local"
    };

    private static Task<HttpResponseMessage> PostBannerAsync(HttpClient client, IDictionary<string, string> fields, byte[]? image = null) =>
        PostMultipartAsync(client, "/admin/banner", fields, image is null ? null : ("image", image, "phong-an.jpg", "image/jpeg"));

    private static async Task<HttpResponseMessage> PostMultipartAsync(HttpClient client, string url, IDictionary<string, string> fields,
        (string Field, byte[] Content, string FileName, string ContentType)? upload)
    {
        var token = await GetAntiforgeryTokenAsync(client, url);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        foreach (var (key, value) in fields)
        {
            form.Add(new StringContent(value), key);
        }

        if (upload is { } u)
        {
            var file = new ByteArrayContent(u.Content);
            file.Headers.ContentType = new MediaTypeHeaderValue(u.ContentType);
            form.Add(file, u.Field, u.FileName);
        }

        return await client.PostAsync(url, form);
    }

    private static Dictionary<string, string> BannerFields(string title = "Bộ sưu tập mùa thu -") => new()
    {
        ["Eyebrow"] = "Mới về tháng 10",
        ["Title"] = title,
        ["TitleHighlight"] = "gỗ óc chó",
        ["Description"] = "Bàn 1m4 và 4 ghế\nGiao trong 7 ngày",
        ["PrimaryButtonText"] = "Xem khuyến mãi",
        ["PrimaryButtonUrl"] = "/products?onSale=true",
        ["SecondaryButtonText"] = "Gọi xưởng",
        ["SecondaryButtonUrl"] = "tel:0900000000",
        ["Stat1Value"] = "36",
        ["Stat1Label"] = "tháng bảo hành",
        ["Stat2Value"] = "",
        ["Stat2Label"] = "",
        ["ImageAlt"] = "Phòng ăn mùa thu"
    };

    // ------------------------------------------------------------------ store name

    [Fact]
    public async Task RenamingTheStore_ChangesTheLogo_TitlesAndFooter_Everywhere()
    {
        var admin = await AdminAsync();
        try
        {
            var saved = await PostFormAsync(admin, "/admin/store", "/admin/store", StoreFields("Gỗ Việt Home", "Home"));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

            var home = await factory.CreateClient().GetStringAsync("/");
            Assert.Contains("<span class=\"brand-name\">Gỗ Việt</span>", home);          // header + footer logo
            Assert.Contains("<span class=\"brand-sub\">Home</span>", home);
            Assert.DoesNotContain(">Nhà Mộc<", home);
            Assert.Contains("<title>Gỗ Việt Home - ", home);
            Assert.Contains("Gỗ Việt Home. Bảo lưu mọi quyền.", home);                    // footer
            Assert.Contains("Chat với Gỗ Việt", home);                                      // chat widget
            Assert.Contains("Vì sao chọn Gỗ Việt", home);
            Assert.Contains("<title>Tất cả sản phẩm | Gỗ Việt Home</title>", await factory.CreateClient().GetStringAsync("/products"));

            var adminPage = await admin.GetStringAsync("/admin");
            Assert.Contains("| Quản trị Gỗ Việt Home</title>", adminPage);
            Assert.Contains("<span class=\"brand-name text-white\">Gỗ Việt</span>", adminPage);
        }
        finally
        {
            await PostFormAsync(admin, "/admin/store", "/admin/store", StoreFields("Nhà Mộc Furniture", "Furniture"));
        }

        Assert.Contains("<span class=\"brand-name\">Nhà Mộc</span>", await factory.CreateClient().GetStringAsync("/"));
    }

    [Fact]
    public async Task StoreForm_ShowsTheLogoPreview()
    {
        var page = await (await AdminAsync()).GetStringAsync("/admin/store");

        Assert.Contains("data-logo-name>Nhà Mộc</span>", page);
        Assert.Contains("data-logo-sub>Furniture</span>", page);
        Assert.Contains("/js/admin-store.js", page);
        Assert.Contains("type=\"file\" id=\"storeLogo\" name=\"logo\"", page);
        Assert.Contains("enctype=\"multipart/form-data\"", page);

        // Each social link has a visible box (an escaped "@" in the TikTok placeholder once rendered it hidden).
        foreach (var field in new[] { "FacebookUrl", "TikTokUrl", "ZaloUrl" })
        {
            var input = System.Text.RegularExpressions.Regex.Match(page, $"<input[^>]*id=\"{field}\"[^>]*>").Value;
            Assert.Contains("type=\"text\"", input);
            Assert.DoesNotContain("hidden", input);
        }

        Assert.Contains("placeholder=\"https://www.tiktok.com/&#64;tenshop\"", page);
    }

    [Fact]
    public async Task LogoPicture_ShowsInHeaderFooterAndAdmin_AsTabIcon_CanHideTheName_AndBeRemoved()
    {
        var admin = await AdminAsync();
        var webRoot = factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        string? url = null;
        try
        {
            var saved = await PostMultipartAsync(admin, "/admin/store", StoreFields("Nhà Mộc Furniture", "Furniture"),
                ("logo", TestImages.Png(400, 400), "logo-shop.png", "image/png"));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
            url = await DbAsync(db => db.StoreInformation.Select(s => s.LogoUrl).SingleAsync());
            Assert.Matches(@"^/uploads/logos/\d{4}/\d{2}/[0-9a-f]{32}-160w\.jpg$", url!);          // 400 × 400 → 160 × 160
            Assert.True(File.Exists(Path.Combine(webRoot, url!.TrimStart('/'))));

            var home = await factory.CreateClient().GetStringAsync("/");
            Assert.Contains($"<img class=\"brand-logo brand-logo-custom\" src=\"{url}\" width=\"160\" height=\"160\" alt=\"\" />", home); // header + footer
            Assert.Contains("<span class=\"brand-name\">Nhà Mộc</span>", home);                    // the name stays next to it
            Assert.Contains($"<link rel=\"icon\" href=\"{url}\" type=\"image/jpeg\" />", home);     // square: tab icon too
            Assert.Contains($"<span class=\"chat-avatar\"><img src=\"{url}\"", home);
            Assert.DoesNotContain("src=\"/images/logo-mark.svg\"", home);
            Assert.Contains($"src=\"{url}\"", await admin.GetStringAsync("/admin"));

            var fields = StoreFields("Nhà Mộc Furniture", "Furniture");
            fields["LogoShowsName"] = "true";
            await PostFormAsync(admin, "/admin/store", "/admin/store", fields);
            home = await factory.CreateClient().GetStringAsync("/");
            Assert.Contains($"src=\"{url}\" width=\"160\" height=\"160\" alt=\"Nhà Mộc Furniture\" />", home);
            Assert.DoesNotContain("<span class=\"brand-name\">", home);                            // the picture contains the name

            fields["RemoveLogo"] = "true";
            await PostFormAsync(admin, "/admin/store", "/admin/store", fields);
            home = await factory.CreateClient().GetStringAsync("/");
            Assert.Contains("<img class=\"brand-logo\" src=\"/images/logo-mark.svg\" width=\"40\" height=\"40\" alt=\"\" />", home);
            Assert.Contains("<span class=\"brand-name\">Nhà Mộc</span>", home);
            Assert.False(File.Exists(Path.Combine(webRoot, url.TrimStart('/'))));
            Assert.Null(await DbAsync(db => db.StoreInformation.Select(s => s.LogoUrl).SingleAsync()));
        }
        finally
        {
            var fields = StoreFields("Nhà Mộc Furniture", "Furniture");
            fields["RemoveLogo"] = "true";
            await PostFormAsync(admin, "/admin/store", "/admin/store", fields);
        }
    }

    [Fact]
    public async Task ShortSocialLinks_AreCompleted_AndShownWithTheirIcons()
    {
        var admin = await AdminAsync();
        var fields = StoreFields("Nhà Mộc Furniture", "Furniture");
        fields["TikTokUrl"] = "@nhamoc.furniture";
        fields["ZaloUrl"] = "0900 000 001";
        fields["FacebookUrl"] = "facebook.com/nhamoc";

        var saved = await PostFormAsync(admin, "/admin/store", "/admin/store", fields);

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var form = await admin.GetStringAsync("/admin/store");
        Assert.Contains("value=\"https://www.tiktok.com/@nhamoc.furniture\"", form);
        Assert.Contains("bi bi-tiktok", form);
        foreach (var page in new[] { "/", "/contact" })
        {
            var html = await factory.CreateClient().GetStringAsync(page);
            Assert.Contains("href=\"https://www.tiktok.com/@nhamoc.furniture\" target=\"_blank\" rel=\"noopener noreferrer\" aria-label=\"TikTok\"", html);
            Assert.Contains("href=\"https://zalo.me/0900000001\"", html);
            Assert.Contains("href=\"https://facebook.com/nhamoc\"", html);
        }
    }

    // ------------------------------------------------------------------ banner

    [Fact]
    public async Task HomePage_ShowsTheBuiltInBanner_UntilOneIsSaved()
    {
        var home = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("<h1 class=\"hero-title mt-3 mb-3\">Bộ bàn ăn gỗ sồi Nga - <em>màu óc chó</em></h1>", home);
        Assert.Contains("href=\"/products?category=bo-ban-an\">Xem bộ bàn ăn", home);
        Assert.Contains("src=\"/images/hero-dining-room.svg\"", home);
        Assert.Contains("<div class=\"stat-value\">24</div>", home);
    }

    [Fact]
    public async Task AdminSavesTheBanner_WithAPicture_TheHomePageShowsIt_AndResetBringsBackTheBuiltInOne()
    {
        var admin = await AdminAsync();
        var webRoot = factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;

        var saved = await PostBannerAsync(admin, BannerFields(), TestImages.Jpeg(1600, 900));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("Đã lưu banner trang chủ.", await admin.GetStringAsync("/admin/banner"));
        var url = await DbAsync(db => db.HomeBanners.Select(b => b.ImageUrl).SingleAsync());
        Assert.Matches(@"^/uploads/banners/\d{4}/\d{2}/[0-9a-f]{32}-1400w\.jpg$", url!);
        var small = url!.Replace("-1400w.jpg", "-700w.jpg");
        try
        {
            var home = await factory.CreateClient().GetStringAsync("/");
            Assert.Contains("<span class=\"hero-eyebrow\"><i class=\"bi bi-stars\" aria-hidden=\"true\"></i>Mới về tháng 10</span>", home);
            Assert.Contains("<h1 class=\"hero-title mt-3 mb-3\">Bộ sưu tập mùa thu - <em>gỗ óc chó</em></h1>", home);
            Assert.Contains("<p class=\"hero-lead mb-4\">Bàn 1m4 và 4 ghế&#xA;Giao trong 7 ngày</p>", home); // line break kept (CSS pre-line)
            Assert.Contains("href=\"/products?onSale=true\">Xem khuyến mãi", home);
            Assert.Contains("href=\"tel:0900000000\">", home);
            Assert.Contains("<div class=\"stat-value\">36</div>", home);
            Assert.DoesNotContain("<div class=\"stat-value\">24</div>", home);          // only the statistics typed in
            Assert.Contains($"src=\"{url}\" srcset=\"{small} 700w, {url} 1400w\"", home);
            Assert.Contains("width=\"1400\" height=\"788\"", home);                     // proportions kept (1600 × 900)
            Assert.Contains("alt=\"Phòng ăn mùa thu\"", home);
            Assert.DoesNotContain("<em>màu óc chó</em>", home);                       // the built-in banner is gone
            Assert.True(File.Exists(Path.Combine(webRoot, url.TrimStart('/'))));
            Assert.True(File.Exists(Path.Combine(webRoot, small.TrimStart('/'))));
        }
        finally
        {
            var reset = await PostFormAsync(admin, "/admin/banner", "/admin/banner/reset", new Dictionary<string, string>());
            Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        }

        Assert.False(File.Exists(Path.Combine(webRoot, url.TrimStart('/'))));
        Assert.False(File.Exists(Path.Combine(webRoot, small.TrimStart('/'))));
        Assert.Contains("Bộ bàn ăn gỗ sồi Nga - <em>màu óc chó</em>", await factory.CreateClient().GetStringAsync("/"));
    }

    [Fact]
    public async Task UnsafeButtonLink_IsRefused_AndTheHomePageDoesNotChange()
    {
        var admin = await AdminAsync();
        var fields = BannerFields("Không được lưu");
        fields["PrimaryButtonUrl"] = "javascript:alert(document.cookie)";

        var response = await PostBannerAsync(admin, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Đường dẫn phải là trang trong website", html);
        Assert.Contains("value=\"Không được lưu\"", html);                              // what was typed stays in the form
        Assert.Equal(0, await DbAsync(db => db.HomeBanners.CountAsync()));
        Assert.DoesNotContain("javascript:alert", await factory.CreateClient().GetStringAsync("/"));
    }

    [Fact]
    public async Task Customers_CannotOpenOrChangeTheBanner()
    {
        var customer = factory.CreateClient();
        await RegisterAsync(customer);

        var page = await customer.GetAsync("/admin/banner");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.StartsWith("https://localhost/account/access-denied", page.Headers.Location?.ToString());

        var token = await GetAntiforgeryTokenAsync(customer, "/account/profile");
        var post = await customer.PostAsync("/admin/banner", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Title"] = "Bị chiếm quyền"
        }));
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        var reset = await customer.PostAsync("/admin/banner/reset", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.NotEqual(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(0, await DbAsync(db => db.HomeBanners.CountAsync()));
    }
}
