using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Admin;

/// <summary>Logo text from the store name, and the home page banner admins edit (/admin/banner), on a real SQLite schema.</summary>
public sealed class BrandingServiceTests : IAsyncLifetime
{
    private ServiceTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync(seedCatalog: false);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action) => _host.RunAsync(sp => action(sp.GetRequiredService<ApplicationDbContext>()));
    private Task<HomeBannerDto> GetBannerAsync() => _host.RunAsync(sp => sp.GetRequiredService<IHomeBannerService>().GetAsync());

    private Task SaveBannerAsync(HomeBannerCommand command, (Stream, string)? image = null) =>
        _host.RunAsync(async sp =>
        {
            await sp.GetRequiredService<IHomeBannerService>().UpdateAsync(command, image);
            return 0;
        });

    private static HomeBannerCommand Banner(Action<HomeBannerCommand>? change = null)
    {
        var command = new HomeBannerCommand
        {
            Eyebrow = "Ưu đãi tháng 10",
            Title = "Bàn ăn gỗ sồi -",
            TitleHighlight = "giảm 10%",
            Description = "Dòng một\r\nDòng hai",
            PrimaryButtonText = "Xem ưu đãi",
            PrimaryButtonUrl = "/products?onSale=true",
            SecondaryButtonText = "Gọi ngay",
            SecondaryButtonUrl = "tel:0900000000",
            Stat1Value = "36",
            Stat1Label = "tháng bảo hành"
        };
        change?.Invoke(command);
        return command;
    }

    private static (Stream, string) Png(string name = "banner.png") => (new MemoryStream(TestImages.Png(40, 30)), name);

    // ------------------------------------------------------------------ logo text

    [Theory]
    [InlineData("Nhà Mộc Furniture", "Furniture", "Nhà Mộc")]
    [InlineData("Nhà Mộc FURNITURE", "Furniture", "Nhà Mộc")]          // case does not matter
    [InlineData("Nhà Mộc Furniture1", "Furniture", "Nhà Mộc Furniture1")] // does not end with it: the whole name
    [InlineData("Gỗ Việt Home", null, "Gỗ Việt Home")]
    [InlineData("Gỗ Việt Home", "  ", "Gỗ Việt Home")]
    [InlineData("Furniture", "Furniture", "Furniture")]                   // never an empty logo
    [InlineData("  Gỗ Việt Home  ", "Home", "Gỗ Việt")]
    public void LogoName_DropsTheSmallLine_OnlyWhenTheNameEndsWithIt(string name, string? subtitle, string expected) =>
        Assert.Equal(expected, StoreInfoDto.BrandNameOf(name, subtitle));

    [Fact]
    public async Task StoreName_AndLogoLine_AreSaved()
    {
        await _host.RunAsync(async sp =>
        {
            await sp.GetRequiredService<IStoreInfoService>().UpdateAsync(new StoreInfoCommand
            {
                Name = "Gỗ Việt Home", LogoSubtitle = " Home ", Address = "1 Đường Test", Hotline = "0900000001", Email = "shop@example.com"
            });
            return 0;
        });

        var store = await _host.RunAsync(sp => sp.GetRequiredService<IStoreInfoService>().GetAsync());
        Assert.Equal(("Gỗ Việt Home", "Home", "Gỗ Việt"), (store.Name, store.LogoSubtitle, store.BrandName));
    }

    [Fact]
    public async Task LogoLine_LongerThan40Characters_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _host.RunAsync(async sp =>
        {
            await sp.GetRequiredService<IStoreInfoService>().UpdateAsync(new StoreInfoCommand
            {
                Name = "Gỗ Việt", LogoSubtitle = new string('x', 41), Address = "1 Đường Test", Hotline = "0900000001", Email = "shop@example.com"
            });
            return 0;
        }));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(StoreInfoCommand.LogoSubtitle)));
    }

    // ------------------------------------------------------------------ logo picture, social links

    private Task<StoreInfoDto> GetStoreAsync() => _host.RunAsync(sp => sp.GetRequiredService<IStoreInfoService>().GetAsync());

    private Task SaveStoreAsync(Action<StoreInfoCommand>? change = null, (Stream, string)? logo = null) =>
        _host.RunAsync(async sp =>
        {
            var command = new StoreInfoCommand { Name = "Nhà Mộc Furniture", LogoSubtitle = "Furniture", Address = "1 Đường Test", Hotline = "0900000001", Email = "shop@example.com" };
            change?.Invoke(command);
            await sp.GetRequiredService<IStoreInfoService>().UpdateAsync(command, logo);
            return 0;
        });

    [Fact]
    public async Task LogoPicture_IsUsed_Replaced_AndRemoved()
    {
        Assert.False((await GetStoreAsync()).HasCustomLogo);

        await SaveStoreAsync(c => c.LogoShowsName = true, Png("logo-1.png"));
        var first = await GetStoreAsync();
        Assert.True(first.HasCustomLogo);
        Assert.StartsWith("/uploads/logos/", first.LogoUrl);
        Assert.True(first.LogoShowsName);
        Assert.False(first.ShowsBrandText);                     // the picture contains the name
        Assert.True(_host.Files.Files.ContainsKey(first.LogoUrl!));

        await SaveStoreAsync(logo: Png("logo-2.png"));
        var second = await GetStoreAsync();
        Assert.NotEqual(first.LogoUrl, second.LogoUrl);
        Assert.False(_host.Files.Files.ContainsKey(first.LogoUrl!)); // the replaced picture is deleted
        Assert.True(second.ShowsBrandText);

        await SaveStoreAsync(c => { c.RemoveLogo = true; c.LogoShowsName = true; });
        var third = await GetStoreAsync();
        Assert.False(third.HasCustomLogo);
        Assert.False(third.LogoShowsName);                      // the house icon never contains the name
        Assert.True(third.ShowsBrandText);
        Assert.Equal(StoreInfoDto.DefaultLogoUrl, third.IconUrl);
        Assert.Empty(_host.Files.Files);
    }

    [Fact]
    public async Task RefusedLogo_LeavesTheStoreAsItWas()
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            SaveStoreAsync(c => c.Name = "Không được lưu", (new MemoryStream("not a picture"u8.ToArray()), "logo.png")));

        Assert.NotEqual("Không được lưu", (await GetStoreAsync()).Name);
        Assert.Empty(_host.Files.Files);
    }

    [Theory]
    [InlineData(400, 400, true)]
    [InlineData(160, 140, true)]
    [InlineData(640, 160, false)] // a name written out: unreadable as a 16 px tab icon
    [InlineData(null, null, false)]
    public void SquareLogos_AlsoServeAsTabIcon(int? width, int? height, bool used)
    {
        var store = new StoreInfoDto("Gỗ Việt", null, null, null, "1 Đường Test", null, "0900000001", "shop@example.com", null, null, null, null, null)
        {
            LogoUrl = "/uploads/logos/2026/10/abc-400w.webp", LogoWidth = width, LogoHeight = height
        };

        Assert.Equal(used ? store.LogoUrl : StoreInfoDto.DefaultLogoUrl, store.IconUrl);
        Assert.Equal(used ? "image/webp" : "image/svg+xml", store.IconContentType);
    }

    [Theory]
    [InlineData(SocialNetwork.TikTok, "@nhamoc.furniture", "https://www.tiktok.com/@nhamoc.furniture")]
    [InlineData(SocialNetwork.TikTok, "tiktok.com/@nhamoc", "https://tiktok.com/@nhamoc")]
    [InlineData(SocialNetwork.TikTok, " https://www.tiktok.com/@nhamoc ", "https://www.tiktok.com/@nhamoc")]
    [InlineData(SocialNetwork.Zalo, "0900 000 000", "https://zalo.me/0900000000")]
    [InlineData(SocialNetwork.Zalo, "zalo.me/0900000000", "https://zalo.me/0900000000")]
    [InlineData(SocialNetwork.Facebook, "www.facebook.com/nhamoc", "https://www.facebook.com/nhamoc")]
    [InlineData(SocialNetwork.Facebook, "javascript:alert(1)", "javascript:alert(1)")] // left for the validator to refuse
    [InlineData(SocialNetwork.Facebook, "http://facebook.com/nhamoc", "http://facebook.com/nhamoc")]
    [InlineData(SocialNetwork.Facebook, "   ", null)]
    public void SocialLinks_AreCompletedFromWhatAdminsPaste(SocialNetwork network, string typed, string? expected) =>
        Assert.Equal(expected, SocialLinks.Normalize(typed, network));

    [Fact]
    public async Task SocialLinks_AreSavedCompleted()
    {
        await SaveStoreAsync(c => { c.TikTokUrl = "@nhamoc"; c.ZaloUrl = "0900 000 001"; c.FacebookUrl = "facebook.com/nhamoc"; });

        var store = await GetStoreAsync();
        Assert.Equal(("https://www.tiktok.com/@nhamoc", "https://zalo.me/0900000001", "https://facebook.com/nhamoc"),
            (store.TikTokUrl, store.ZaloUrl, store.FacebookUrl));
    }

    // ------------------------------------------------------------------ banner

    [Fact]
    public async Task WithoutSavedBanner_TheBuiltInOneIsShown()
    {
        var banner = await GetBannerAsync();

        Assert.False(banner.IsCustomized);
        Assert.Equal("Bộ bàn ăn gỗ sồi Nga -", banner.Title);
        Assert.Equal(3, banner.Stats.Count);
        Assert.Equal(HomeBannerDefaults.ImageUrl, banner.ImageUrl);
        Assert.Null(banner.PrimaryButtonUrl); // the main product line
    }

    [Fact]
    public async Task SavedBanner_ReplacesTheBuiltInOne_AndIsAudited()
    {
        await SaveBannerAsync(Banner(c => { c.Stat2Value = " "; c.Stat2Label = ""; c.Stat3Value = "5"; c.Stat3Label = "showroom"; }));

        var banner = await GetBannerAsync();
        Assert.True(banner.IsCustomized);
        Assert.Equal(("Ưu đãi tháng 10", "Bàn ăn gỗ sồi -", "giảm 10%"), (banner.Eyebrow, banner.Title, banner.TitleHighlight));
        Assert.Equal("Dòng một\nDòng hai", banner.Description);
        Assert.Equal(("/products?onSale=true", "tel:0900000000"), (banner.PrimaryButtonUrl, banner.SecondaryButtonUrl));
        Assert.Equal(new[] { new HomeBannerStat("36", "tháng bảo hành"), new HomeBannerStat("5", "showroom") }, banner.Stats); // the empty one is hidden
        Assert.False(banner.HasCustomImage);
        Assert.Equal(HomeBannerDefaults.ImageAlt, banner.ImageAlt);
        Assert.True(await Db(db => db.AuditLogs.AnyAsync(l => l.EntityName == "HomeBanner")));
    }

    [Fact]
    public async Task EmptyOptionalTexts_HideTheirElements()
    {
        await SaveBannerAsync(new HomeBannerCommand { Title = "Chỉ có tiêu đề", SecondaryButtonUrl = "/contact" });

        var banner = await GetBannerAsync();
        Assert.Equal("Chỉ có tiêu đề", banner.Title);
        Assert.Null(banner.Eyebrow);
        Assert.Null(banner.PrimaryButtonText);
        Assert.Null(banner.SecondaryButtonText);
        Assert.Null(banner.SecondaryButtonUrl); // no text: no button, the link is dropped
        Assert.Empty(banner.Stats);
    }

    [Fact]
    public async Task UploadedPicture_IsUsed_ReplacedPicturesAreDeleted_AndRemovingGoesBackToTheIllustration()
    {
        await SaveBannerAsync(Banner(c => c.ImageAlt = "Phòng ăn mẫu"), Png("lan-1.png"));
        var first = await GetBannerAsync();
        Assert.True(first.HasCustomImage);
        Assert.StartsWith("/uploads/banners/", first.ImageUrl);
        Assert.Equal("Phòng ăn mẫu", first.ImageAlt);
        Assert.True(_host.Files.Files.ContainsKey(first.ImageUrl));

        await SaveBannerAsync(Banner(), Png("lan-2.png"));
        var second = await GetBannerAsync();
        Assert.NotEqual(first.ImageUrl, second.ImageUrl);
        Assert.False(_host.Files.Files.ContainsKey(first.ImageUrl));         // the replaced picture is gone
        Assert.Equal("Bàn ăn gỗ sồi - giảm 10%", second.ImageAlt);           // no description: the title

        await SaveBannerAsync(Banner(c => { c.RemoveImage = true; c.ImageAlt = "bỏ qua"; }));
        var third = await GetBannerAsync();
        Assert.False(third.HasCustomImage);
        Assert.Equal((HomeBannerDefaults.ImageUrl, HomeBannerDefaults.ImageAlt), (third.ImageUrl, third.ImageAlt));
        Assert.Empty(_host.Files.Files);
    }

    [Fact]
    public async Task RefusedPicture_LeavesTheBannerAsItWas()
    {
        await SaveBannerAsync(Banner());

        await Assert.ThrowsAsync<AppValidationException>(() =>
            SaveBannerAsync(Banner(c => c.Title = "Không được lưu"), (new MemoryStream("not a picture"u8.ToArray()), "virus.png")));

        Assert.Equal("Bàn ăn gỗ sồi -", (await GetBannerAsync()).Title);
        Assert.Empty(_host.Files.Files);
    }

    [Fact]
    public async Task Reset_GoesBackToTheBuiltInBanner_AndDeletesThePicture()
    {
        await SaveBannerAsync(Banner(), Png());
        await _host.RunAsync(async sp =>
        {
            await sp.GetRequiredService<IHomeBannerService>().ResetAsync();
            return 0;
        });

        Assert.False((await GetBannerAsync()).IsCustomized);
        Assert.Empty(_host.Files.Files);
        Assert.Equal(0, await Db(db => db.HomeBanners.CountAsync()));
    }

    [Theory]
    [InlineData("/products?category=bo-ban-an", true)]
    [InlineData("/", true)]
    [InlineData("https://zalo.me/0900000000", true)]
    [InlineData("tel:0900000000", true)]
    [InlineData("tel:+84 900", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("JavaScript:alert(1)", false)]
    [InlineData("//evil.example/phish", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("http://example.com", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("/san pham", false)]
    public void ButtonLinks_OnlyThisSite_Https_OrPhone(string link, bool accepted) =>
        Assert.Equal(accepted, HomeBannerCommandValidator.BeSafeLink(link));

    [Fact]
    public async Task InvalidBanner_IsRefused_WithAMessagePerField()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => SaveBannerAsync(Banner(c =>
        {
            c.Title = " ";
            c.PrimaryButtonUrl = "javascript:alert(1)";
            c.SecondaryButtonUrl = null;     // text without link
            c.Stat1Label = null;             // number without text
            c.Stat2Label = "chỉ có mô tả";   // text without number
        })));

        Assert.True(ex.FieldErrors.ContainsKey(nameof(HomeBannerCommand.Title)));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(HomeBannerCommand.PrimaryButtonUrl)));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(HomeBannerCommand.SecondaryButtonUrl)));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(HomeBannerCommand.Stat1Label)));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(HomeBannerCommand.Stat2Value)));
        Assert.False((await GetBannerAsync()).IsCustomized);
    }
}
