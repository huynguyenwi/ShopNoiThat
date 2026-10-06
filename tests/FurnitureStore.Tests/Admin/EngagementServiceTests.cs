using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Admin;

/// <summary>Reviews, contact messages and store information (Phase 6).</summary>
public sealed class EngagementServiceTests : IAsyncLifetime
{
    private const string Sku = "KTT-PINE";
    private ServiceTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> action) => _host.RunAsync(action);
    private Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action) => Run(sp => action(sp.GetRequiredService<ApplicationDbContext>()));
    private Task<T> Reviews<T>(Func<IReviewService, Task<T>> action) => Run(sp => action(sp.GetRequiredService<IReviewService>()));
    private Task Reviews(Func<IReviewService, Task> action) => _host.RunAsync(sp => action(sp.GetRequiredService<IReviewService>()));

    private Task<int> ProductIdAsync() => Db(db => db.Products.Where(p => p.Sku == Sku).Select(p => p.Id).SingleAsync());

    private async Task<string> CustomerWithDeliveredOrderAsync()
    {
        var userId = await AdminServiceTests.CreateCustomerAsync(_host);
        var placed = await AdminServiceTests.PlaceOrderAsync(_host, userId, Sku);
        await AdminServiceTests.DeliverAsync(_host, placed.OrderId);
        return userId;
    }

    private static ReviewCommand Review(int rating, string comment = "Kệ chắc chắn, gỗ đẹp, giao hàng nhanh.") =>
        new() { Rating = rating, Title = "Hài lòng", Comment = comment };

    private static IReadOnlyList<(Stream, string)> NoImages => [];

    // ------------------------------------------------------------------ Reviews

    [Fact]
    public async Task Eligibility_RequiresSignInAndADeliveredPurchase()
    {
        var productId = await ProductIdAsync();

        var anonymous = await Reviews(r => r.GetEligibilityAsync(null, productId));
        Assert.False(anonymous.CanReview);

        var buyer = await AdminServiceTests.CreateCustomerAsync(_host);
        var placed = await AdminServiceTests.PlaceOrderAsync(_host, buyer, Sku);
        Assert.False((await Reviews(r => r.GetEligibilityAsync(buyer, productId))).CanReview); // ordered, not delivered yet

        await AdminServiceTests.DeliverAsync(_host, placed.OrderId);
        Assert.True((await Reviews(r => r.GetEligibilityAsync(buyer, productId))).CanReview);
    }

    [Fact]
    public async Task Submit_WithoutPurchase_IsForbidden()
    {
        var stranger = await AdminServiceTests.CreateCustomerAsync(_host);
        var productId = await ProductIdAsync();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Reviews(r => r.SubmitAsync(stranger, "Người lạ", productId, Review(1), NoImages)));
        Assert.Equal(0, await Db(db => db.Reviews.CountAsync()));
    }

    [Fact]
    public async Task Submit_SavesVerifiedReview_UpdatesProductRating_AndNotifiesAdmin()
    {
        var first = await CustomerWithDeliveredOrderAsync();
        var second = await CustomerWithDeliveredOrderAsync();
        var productId = await ProductIdAsync();

        await Reviews(r => r.SubmitAsync(first, "Anh Minh", productId, Review(5), [(InMemoryFileStorage.TinyPng(), "that-te.png")]));
        await Reviews(r => r.SubmitAsync(second, "Chị Lan", productId, Review(4), NoImages));

        var product = await Db(db => db.Products.AsNoTracking().SingleAsync(p => p.Id == productId));
        Assert.Equal(4.5m, product.AverageRating);
        Assert.Equal(2, product.ReviewCount);

        var page = await Reviews(r => r.GetForProductAsync(productId));
        Assert.Equal(2, page.Stats.Count);
        Assert.Equal(1, page.Stats.Distribution[4]);
        Assert.Equal(1, page.Stats.Distribution[3]);
        Assert.All(page.Reviews.Items, review => Assert.True(review.IsVerifiedPurchase));
        Assert.Single(page.Reviews.Items.Single(x => x.ReviewerName == "Anh Minh").ImageUrls);
        Assert.Equal(2, await Db(db => db.Notifications.CountAsync(n => n.RecipientRole == AppRoles.Admin && n.Type == NotificationType.NewReview)));
    }

    [Fact]
    public async Task Submit_Twice_UpdatesTheSameReview_AndReplacesImages()
    {
        var userId = await CustomerWithDeliveredOrderAsync();
        var productId = await ProductIdAsync();

        await Reviews(r => r.SubmitAsync(userId, "Anh Minh", productId, Review(2), [(InMemoryFileStorage.TinyPng(), "a.png")]));
        var firstImage = Assert.Single(_host.Files.Files.Keys);

        await Reviews(r => r.SubmitAsync(userId, "Anh Minh", productId, Review(5, "Dùng một tháng thấy rất ổn, đổi lại 5 sao."), [(InMemoryFileStorage.TinyPng(), "b.png")]));

        var review = await Db(db => db.Reviews.Include(x => x.Images).AsNoTracking().SingleAsync());
        Assert.Equal(5, review.Rating);
        Assert.StartsWith("Dùng một tháng", review.Comment);
        Assert.DoesNotContain(firstImage, _host.Files.Files.Keys);   // old file removed after the new one was saved
        Assert.Equal(Assert.Single(review.Images).Url, Assert.Single(_host.Files.Files.Keys));
        Assert.Equal(5m, await Db(db => db.Products.Where(p => p.Id == productId).Select(p => p.AverageRating).SingleAsync()));

        var eligibility = await Reviews(r => r.GetEligibilityAsync(userId, productId));
        Assert.Equal(5, eligibility.Existing?.Rating);
    }

    [Fact]
    public async Task Submit_Validation_RejectsBadInputAndFiles_WithoutLeavingFiles()
    {
        var userId = await CustomerWithDeliveredOrderAsync();
        var productId = await ProductIdAsync();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Reviews(r => r.SubmitAsync(userId, "A", productId, new ReviewCommand { Rating = 6, Comment = "ngắn" }, NoImages)));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(ReviewCommand.Rating)));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(ReviewCommand.Comment)));

        var tooMany = Enumerable.Range(0, 4).Select(i => ((Stream)InMemoryFileStorage.TinyPng(), $"{i}.png")).ToList();
        await Assert.ThrowsAsync<AppValidationException>(() => Reviews(r => r.SubmitAsync(userId, "A", productId, Review(5), tooMany)));

        // A valid image followed by a fake one (HTML renamed to .png): nothing may be kept.
        var fake = new MemoryStream("<script>alert(1)</script>"u8.ToArray());
        await Assert.ThrowsAsync<AppValidationException>(() => Reviews(r => r.SubmitAsync(userId, "A", productId, Review(5),
            [(InMemoryFileStorage.TinyPng(), "ok.png"), (fake, "evil.png")])));

        Assert.Empty(_host.Files.Files);
        Assert.Equal(0, await Db(db => db.Reviews.CountAsync()));
    }

    [Fact]
    public async Task Admin_HideReplyDelete_KeepsRatingInSync()
    {
        var first = await CustomerWithDeliveredOrderAsync();
        var second = await CustomerWithDeliveredOrderAsync();
        var productId = await ProductIdAsync();
        var good = await Reviews(r => r.SubmitAsync(first, "Anh Minh", productId, Review(5), [(InMemoryFileStorage.TinyPng(), "a.png")]));
        var bad = await Reviews(r => r.SubmitAsync(second, "Spam", productId, Review(1), NoImages));

        await Reviews(r => r.SetHiddenAsync(bad.Id, true));
        var afterHide = await Reviews(r => r.GetForProductAsync(productId));
        Assert.Equal(1, afterHide.Stats.Count);
        Assert.Equal(5m, afterHide.Stats.Average);
        Assert.DoesNotContain(afterHide.Reviews.Items, x => x.Id == bad.Id);
        Assert.Equal(1, await Db(db => db.Products.Where(p => p.Id == productId).Select(p => p.ReviewCount).SingleAsync()));

        var hiddenOnly = await Reviews(r => r.SearchAsync(new AdminReviewQuery { Hidden = true }));
        Assert.Equal(bad.Id, Assert.Single(hiddenOnly.Items).Id);

        await Reviews(r => r.ReplyAsync(good.Id, "Cảm ơn anh đã tin chọn Nhà Mộc!"));
        Assert.Equal("Cảm ơn anh đã tin chọn Nhà Mộc!", (await Reviews(r => r.GetForProductAsync(productId))).Reviews.Items.Single().AdminReply);

        await Reviews(r => r.DeleteAsync(good.Id));
        Assert.Empty(_host.Files.Files);
        Assert.Equal(0, await Db(db => db.Products.Where(p => p.Id == productId).Select(p => p.ReviewCount).SingleAsync()));
        Assert.Equal(0m, await Db(db => db.Products.Where(p => p.Id == productId).Select(p => p.AverageRating).SingleAsync()));
    }

    [Fact]
    public async Task LatestReviews_ForHomePage_ExcludeHidden()
    {
        var first = await CustomerWithDeliveredOrderAsync();
        var second = await CustomerWithDeliveredOrderAsync();
        var productId = await ProductIdAsync();
        await Reviews(r => r.SubmitAsync(first, "Anh Minh", productId, Review(5), NoImages));
        var hidden = await Reviews(r => r.SubmitAsync(second, "Ẩn", productId, Review(3), NoImages));
        await Reviews(r => r.SetHiddenAsync(hidden.Id, true));

        var latest = await Reviews(r => r.GetLatestAsync());

        var item = Assert.Single(latest);
        Assert.Equal("Anh Minh", item.ReviewerName);
        Assert.Equal(await Db(db => db.Products.Where(p => p.Id == productId).Select(p => p.Slug).SingleAsync()), item.ProductSlug);
    }

    // ------------------------------------------------------------------ Contact

    [Fact]
    public async Task Contact_InvalidInput_ReturnsFieldErrors()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Run(sp => sp.GetRequiredService<IContactService>()
            .SubmitAsync(new ContactCommand { FullName = "", Phone = "123", Email = "abc", Message = "ngắn" }, null, null)));

        Assert.True(ex.FieldErrors.ContainsKey(nameof(ContactCommand.FullName)));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(ContactCommand.Phone)));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(ContactCommand.Email)));
        Assert.True(ex.FieldErrors.ContainsKey(nameof(ContactCommand.Message)));
    }

    [Fact]
    public async Task Contact_Submit_Open_Reply_Workflow()
    {
        var contacts = (Func<IContactService, Task<ContactMessageDto>> f) => Run(sp => f(sp.GetRequiredService<IContactService>()));
        var id = await Run(sp => sp.GetRequiredService<IContactService>().SubmitAsync(new ContactCommand
        {
            FullName = "  Trần Thị B ",
            Phone = "0987654321",
            Email = "b@example.com",
            Subject = "Đặt bàn theo kích thước",
            Message = "Tôi muốn đặt bàn ăn 1m8 gỗ sồi, báo giá giúp."
        }, null, "203.0.113.9"));

        Assert.Equal(1, await Run(sp => sp.GetRequiredService<IContactService>().CountNewAsync()));
        Assert.True(await Db(db => db.Notifications.AnyAsync(n => n.Type == NotificationType.NewContactMessage && n.Link == $"/admin/contacts/details/{id}")));

        var opened = await contacts(c => c.OpenAsync(id));
        Assert.Equal("Trần Thị B", opened.FullName);
        Assert.Equal(ContactMessageStatus.Read, opened.Status);
        Assert.Equal(0, await Run(sp => sp.GetRequiredService<IContactService>().CountNewAsync()));

        await Run(async sp => { await sp.GetRequiredService<IContactService>().UpdateAsync(id, ContactMessageStatus.Replied, "Đã gọi lại"); return 0; });
        var replied = await contacts(c => c.OpenAsync(id));
        Assert.Equal(ContactMessageStatus.Replied, replied.Status);
        Assert.NotNull(replied.RepliedAt);
        Assert.Equal("Đã gọi lại", replied.AdminNote);

        var search = await Run(sp => sp.GetRequiredService<IContactService>().ListAsync(null, "sồi"));
        Assert.Single(search.Items);
    }

    // ------------------------------------------------------------------ Store information

    [Fact]
    public async Task StoreInfo_FallsBackToSettings_ThenUsesSavedValues()
    {
        var store = (Func<IStoreInfoService, Task<StoreInfoDto>> f) => Run(sp => f(sp.GetRequiredService<IStoreInfoService>()));
        var initial = await store(s => s.GetAsync());
        Assert.NotNull(initial);

        await Run(async sp =>
        {
            await sp.GetRequiredService<IStoreInfoService>().UpdateAsync(new StoreInfoCommand
            {
                Name = "Nhà Mộc Test",
                Address = "1 Đường Test",
                Hotline = "0900 000 001",
                Email = "shop@example.com",
                FacebookUrl = "https://facebook.com/nhamoc",
                GoogleMapsEmbedUrl = "https://www.google.com/maps?q=10.77,106.70&output=embed"
            });
            return 0;
        });

        var saved = await store(s => s.GetAsync());
        Assert.Equal("Nhà Mộc Test", saved.Name);
        Assert.Equal("0900000001", saved.HotlineDigits);
        Assert.True(await Db(db => db.AuditLogs.AnyAsync(l => l.EntityName == "StoreInfo")));
    }

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("http://facebook.com/nhamoc", null)]
    [InlineData(null, "https://evil.example.com/maps")]
    [InlineData(null, "javascript:alert(1)")]
    public async Task StoreInfo_RejectsUnsafeLinks(string? facebook, string? map)
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Run(async sp =>
        {
            await sp.GetRequiredService<IStoreInfoService>().UpdateAsync(new StoreInfoCommand
            {
                Name = "Nhà Mộc",
                Address = "1 Đường Test",
                Hotline = "0900000001",
                Email = "shop@example.com",
                FacebookUrl = facebook,
                GoogleMapsEmbedUrl = map
            });
            return 0;
        }));

        Assert.True(ex.FieldErrors.ContainsKey(facebook is not null ? nameof(StoreInfoCommand.FacebookUrl) : nameof(StoreInfoCommand.GoogleMapsEmbedUrl)));
    }
}
