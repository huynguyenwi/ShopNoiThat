using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

/// <summary>Phase 6 through HTTP: admin back-office pages, order / user management, reviews, contact and store info.</summary>
public sealed partial class AdminWebTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private Task<HttpClient> AdminAsync() =>
        CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

    private Task<HttpClient> CustomerAsync() =>
        CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword);

    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private async Task<string[]> AdminPagesAsync()
    {
        var orderId = await DbAsync(db => db.Orders.Select(o => o.Id).FirstAsync());
        var customerId = await DbAsync(db => db.Users.Where(u => u.Email == FurnitureStoreWebApplicationFactory.UserEmail).Select(u => u.Id).SingleAsync());
        var contactId = await DbAsync(db => db.ContactMessages.Select(c => c.Id).FirstAsync());
        return
        [
            "/admin", "/admin/orders", $"/admin/orders/details/{orderId}", "/admin/orders?status=Pending",
            "/admin/customers", $"/admin/customers/details/{customerId}", "/admin/reviews", "/admin/reviews?hidden=true",
            "/admin/contacts", $"/admin/contacts/details/{contactId}", "/admin/store", "/admin/audit-logs", "/admin/notifications",
            "/admin/products", "/admin/categories", "/admin/attributes/colors"
        ];
    }

    // ------------------------------------------------------------------ Access control

    [Fact]
    public async Task EveryAdminPage_LoadsForAdmin_AndIsDeniedToCustomersAndAnonymous()
    {
        var admin = await AdminAsync();
        var customer = await CustomerAsync();
        var anonymous = factory.CreateClient();

        foreach (var page in await AdminPagesAsync())
        {
            var ok = await admin.GetAsync(page);
            Assert.True(ok.StatusCode == HttpStatusCode.OK, $"{page} returned {(int)ok.StatusCode} for admin");

            var denied = await customer.GetAsync(page);
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("/account/access-denied", denied.Headers.Location!.ToString());

            var login = await anonymous.GetAsync(page);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Contains("/account/login", login.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task AdminPostEndpoints_RejectCustomers_EvenWithAValidToken()
    {
        var (customer, orderId, _, _) = await PlaceOrderThroughUiAsync();

        // The customer owns the order and has a valid anti-forgery token, but is not an admin.
        var response = await PostFormAsync(customer, "/account/profile", $"/admin/orders/status/{orderId}",
            new Dictionary<string, string> { ["status"] = "Confirmed" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/access-denied", response.Headers.Location!.ToString());
        Assert.Equal(OrderStatus.Pending, await DbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync()));
    }

    [Fact]
    public async Task EveryPostFormOnAdminPages_CarriesAnAntiforgeryToken()
    {
        var admin = await AdminAsync();

        foreach (var page in await AdminPagesAsync())
        {
            var html = await admin.GetStringAsync(page);
            foreach (Match form in PostFormRegex().Matches(html))
            {
                Assert.True(form.Value.Contains("__RequestVerificationToken"), $"POST form without anti-forgery token on {page}: {form.Value[..Math.Min(150, form.Value.Length)]}");
            }
        }
    }

    [Fact]
    public async Task Dashboard_ShowsKpisAndChartData_FromDemoActivity()
    {
        var admin = await AdminAsync();

        var html = await admin.GetStringAsync("/admin");

        Assert.Contains("id=\"dashboardData\"", html);
        Assert.Contains("revenueByDay", html);
        Assert.Contains("chart.umd.min.js", html);
        Assert.Contains("Doanh thu", html);
        Assert.Matches("DH\\d{6}-", html); // recent orders from the demo data
    }

    // ------------------------------------------------------------------ Orders

    [Fact]
    public async Task Admin_ChangesOrderStatus_ThroughTheForm_AndStaleVersionIsReported()
    {
        var admin = await AdminAsync();
        var (_, orderId, _, _) = await PlaceOrderThroughUiAsync();
        var version = await DbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.Version).SingleAsync());
        var detailUrl = $"/admin/orders/details/{orderId}";

        var confirm = await PostFormAsync(admin, detailUrl, $"/admin/orders/status/{orderId}", new Dictionary<string, string>
        {
            ["status"] = "Confirmed",
            ["version"] = version.ToString()
        });
        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
        Assert.Equal(OrderStatus.Confirmed, await DbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync()));

        // Same (now stale) version again: rejected with a message, status unchanged.
        await PostFormAsync(admin, detailUrl, $"/admin/orders/status/{orderId}", new Dictionary<string, string>
        {
            ["status"] = "Processing",
            ["version"] = version.ToString()
        });
        Assert.Equal(OrderStatus.Confirmed, await DbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync()));
        Assert.Contains("vừa được cập nhật bởi người khác", await admin.GetStringAsync(detailUrl));
    }

    // ------------------------------------------------------------------ Users

    [Fact]
    public async Task Admin_LocksAndUnlocksACustomer()
    {
        var customerClient = factory.CreateClient();
        var email = await RegisterAsync(customerClient);
        var userId = await DbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        var admin = await AdminAsync();

        var lockResponse = await PostFormAsync(admin, $"/admin/customers/details/{userId}", $"/admin/customers/lock/{userId}",
            new Dictionary<string, string> { ["reason"] = "Spam" });
        Assert.Equal(HttpStatusCode.Redirect, lockResponse.StatusCode);

        var login = await LoginAsync(factory.CreateClient(), email, "Khach@Hang123");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); // form re-displayed with an error, no auth cookie
        Assert.DoesNotContain(login.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], c => c.StartsWith(".NhaMoc.Auth="));

        await PostFormAsync(admin, $"/admin/customers/details/{userId}", $"/admin/customers/unlock/{userId}", new Dictionary<string, string>());
        var loginAgain = await LoginAsync(factory.CreateClient(), email, "Khach@Hang123");
        Assert.Equal(HttpStatusCode.Redirect, loginAgain.StatusCode);
    }

    [Fact]
    public async Task Admin_CannotLockThemselves()
    {
        var admin = await AdminAsync();
        var adminId = await DbAsync(db => db.Users.Where(u => u.Email == FurnitureStoreWebApplicationFactory.AdminEmail).Select(u => u.Id).SingleAsync());

        await PostFormAsync(admin, $"/admin/customers/details/{adminId}", $"/admin/customers/lock/{adminId}", new Dictionary<string, string>());

        Assert.True(await DbAsync(db => db.Users.Where(u => u.Id == adminId).Select(u => u.IsActive).SingleAsync()));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin")).StatusCode);
    }

    // ------------------------------------------------------------------ Contact

    [Fact]
    public async Task ContactPage_ShowsStoreInfo_AndAcceptsValidMessages()
    {
        var client = factory.CreateClient();
        var page = await client.GetStringAsync("/contact");
        Assert.Contains("Gửi tin nhắn cho chúng tôi", page);
        Assert.Contains("Showroom", page);

        var ok = await PostFormAsync(client, "/contact", "/contact", new Dictionary<string, string>
        {
            ["Command.FullName"] = "Lê Văn Liên Hệ",
            ["Command.Phone"] = "0987654321",
            ["Command.Email"] = "lienhe@example.com",
            ["Command.Subject"] = "Tư vấn sofa",
            ["Command.Message"] = "Cho mình hỏi sofa góc chữ L còn màu xám không?"
        });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.True(await DbAsync(db => db.ContactMessages.AnyAsync(c => c.Email == "lienhe@example.com" && c.Status == ContactMessageStatus.New)));
        Assert.Contains("Tin nhắn đã được gửi", await client.GetStringAsync("/contact"));
    }

    [Fact]
    public async Task ContactPage_InvalidInput_ShowsFieldErrors_AndHoneypotIsDropped()
    {
        var client = factory.CreateClient();
        var invalid = await PostFormAsync(client, "/contact", "/contact", new Dictionary<string, string>
        {
            ["Command.FullName"] = "",
            ["Command.Phone"] = "123",
            ["Command.Email"] = "khong-hop-le",
            ["Command.Message"] = "ngắn"
        });
        var html = await invalid.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Vui lòng nhập họ tên.", html);
        Assert.Contains("Email không hợp lệ.", html);

        var bot = await PostFormAsync(client, "/contact", "/contact", new Dictionary<string, string>
        {
            ["Command.FullName"] = "Bot",
            ["Command.Phone"] = "0987654321",
            ["Command.Email"] = "bot@example.com",
            ["Command.Message"] = "Buy cheap followers now!!!",
            ["website"] = "https://spam.example.com"
        });
        Assert.Equal(HttpStatusCode.Redirect, bot.StatusCode);
        Assert.False(await DbAsync(db => db.ContactMessages.AnyAsync(c => c.Email == "bot@example.com")));
    }

    [Fact]
    public async Task ContactForm_WithoutAntiforgeryToken_IsRejected()
    {
        var client = factory.CreateClient();
        await client.GetAsync("/contact");

        var response = await client.PostAsync("/contact", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Command.FullName"] = "CSRF",
            ["Command.Phone"] = "0987654321",
            ["Command.Email"] = "csrf@example.com",
            ["Command.Message"] = "Tin nhắn giả mạo từ trang khác."
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await DbAsync(db => db.ContactMessages.AnyAsync(c => c.Email == "csrf@example.com")));
    }

    [Fact]
    public async Task ContactForm_IsRateLimited()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:FormPermitsPerMinute", "2"));
        var client = limited.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var fields = new Dictionary<string, string>
        {
            ["Command.FullName"] = "Khách",
            ["Command.Phone"] = "0987654321",
            ["Command.Email"] = "ratelimit@example.com",
            ["Command.Message"] = "Kiểm tra giới hạn số lần gửi."
        };

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await PostFormAsync(client, "/contact", "/contact", fields)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Redirect, HttpStatusCode.Redirect, HttpStatusCode.TooManyRequests], statuses);
    }

    // ------------------------------------------------------------------ Store info

    [Fact]
    public async Task StoreInfo_EditedByAdmin_AppearsInFooterAndContactPage()
    {
        var admin = await AdminAsync();
        var fields = new Dictionary<string, string>
        {
            ["Name"] = "Nhà Mộc Test Store",
            ["Address"] = "99 Đường Thử Nghiệm, TP. Hồ Chí Minh",
            ["Hotline"] = "0909 123 456",
            ["Email"] = "store@example.com",
            ["OpeningHours"] = "8:00 - 20:00",
            ["FacebookUrl"] = "javascript:alert(1)"
        };

        var rejected = await PostFormAsync(admin, "/admin/store", "/admin/store", fields);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Contains("https://", await rejected.Content.ReadAsStringAsync());

        fields["FacebookUrl"] = "https://facebook.com/nhamoc.test";
        var saved = await PostFormAsync(admin, "/admin/store", "/admin/store", fields);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var home = await factory.CreateClient().GetStringAsync("/contact");
        Assert.Contains("99 Đường Thử Nghiệm", home);
        Assert.Contains("tel:0909123456", home);
        Assert.Contains("https://facebook.com/nhamoc.test", home);
        Assert.DoesNotContain("javascript:alert", home);
    }

    // ------------------------------------------------------------------ Reviews

    [Fact]
    public async Task ProductPage_ShowsReviewsSection_AndHomeShowsTestimonials()
    {
        var client = factory.CreateClient();
        var slug = await DbAsync(db => db.Products.Where(p => p.ReviewCount > 0).OrderByDescending(p => p.ReviewCount).Select(p => p.Slug).FirstAsync());

        var detail = await client.GetStringAsync($"/products/{slug}");
        Assert.Contains("id=\"reviews\"", detail);
        Assert.Contains("Đánh giá từ khách hàng", detail);
        Assert.Contains("Đã mua hàng", detail);
        Assert.Contains("Đăng nhập", detail);

        var fragment = await client.GetAsync($"/products/{slug}/reviews?page=1");
        Assert.Equal(HttpStatusCode.OK, fragment.StatusCode);
        Assert.StartsWith("<div id=\"reviewList\"", (await fragment.Content.ReadAsStringAsync()).TrimStart());

        Assert.Contains("Đánh giá từ khách hàng đã mua", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task Review_Anonymous_RedirectsToLogin_AndNonBuyerIsRefused()
    {
        var slug = await DbAsync(db => db.Products.Select(p => p.Slug).FirstAsync());
        var fields = new Dictionary<string, string> { ["Review.Rating"] = "5", ["Review.Comment"] = "Sản phẩm rất đẹp và chắc chắn." };

        var anonymous = await PostFormAsync(factory.CreateClient(), "/contact", $"/products/{slug}/reviews", fields);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/account/login", anonymous.Headers.Location!.ToString());

        var nonBuyer = await PostFormAsync(await RegisterAndReturnAsync(), $"/products/{slug}", $"/products/{slug}/reviews", fields);
        Assert.Equal(HttpStatusCode.BadRequest, nonBuyer.StatusCode);
        Assert.Contains("Chỉ khách hàng đã mua", await nonBuyer.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Review_BuyerWithDeliveredOrder_CanPostWithImage()
    {
        // Customer buys through the real checkout, admin delivers through the back-office.
        var (buyer, orderId, productId, slug) = await PlaceOrderThroughUiAsync();

        var admin = await AdminAsync();
        foreach (var status in new[] { "Confirmed", "Processing", "Shipping", "Delivered" })
        {
            await PostFormAsync(admin, $"/admin/orders/details/{orderId}", $"/admin/orders/status/{orderId}", new Dictionary<string, string> { ["status"] = status });
        }
        Assert.Equal(OrderStatus.Delivered, await DbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync()));

        // The review form is now offered, and a multipart post with a real PNG header is accepted.
        var page = await buyer.GetStringAsync($"/products/{slug}");
        Assert.Contains("Viết đánh giá", page);
        var token = WebUtility.HtmlDecode(TokenRegex().Match(page).Groups[1].Value);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("4"), "Review.Rating" },
            { new StringContent("Giao đúng hẹn"), "Review.Title" },
            { new StringContent("Gỗ đẹp, lắp đặt cẩn thận, rất đáng tiền."), "Review.Comment" },
            { new ByteArrayContent(InMemoryFileStorage.TinyPng().ToArray()), "images", "anh-that.png" }
        };
        var posted = await buyer.PostAsync($"/products/{slug}/reviews", form);

        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.EndsWith("#reviews", posted.Headers.Location!.ToString());
        var review = await DbAsync(db => db.Reviews.Include(r => r.Images).SingleAsync(r => r.ProductId == productId && r.Title == "Giao đúng hẹn"));
        Assert.True(review.IsVerifiedPurchase);
        var imageUrl = Assert.Single(review.Images).Url;
        Assert.StartsWith("/uploads/reviews/", imageUrl);
        Assert.Contains("Gỗ đẹp, lắp đặt cẩn thận", await buyer.GetStringAsync($"/products/{slug}"));

        // Clean up the uploaded file written by the real storage service.
        var webRoot = factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var file = Path.Combine(webRoot, imageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>Registers a new customer, adds one unit to the cart through the API and checks out (COD). Returns the buyer and the order id.</summary>
    private async Task<(HttpClient Buyer, int OrderId, int ProductId, string Slug)> PlaceOrderThroughUiAsync()
    {
        var buyer = await RegisterAndReturnAsync();
        var (variantId, productId, slug) = await DbAsync(async db =>
        {
            var v = await db.ProductVariants.Include(x => x.Product).Where(x => x.StockQuantity >= 2 && x.Product.Status == ProductStatus.Active)
                .OrderBy(x => x.Id).FirstAsync();
            return (v.Id, v.ProductId, v.Product.Slug);
        });
        var csrf = WebUtility.HtmlDecode(CsrfMetaRegex().Match(await buyer.GetStringAsync("/")).Groups[1].Value);
        var add = new HttpRequestMessage(HttpMethod.Post, "/api/cart") { Content = JsonContent.Create(new { variantId, quantity = 1 }) };
        add.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.OK, (await buyer.SendAsync(add)).StatusCode);

        var placed = await PostFormAsync(buyer, "/checkout", "/checkout", new Dictionary<string, string>
        {
            ["Command.FullName"] = "Người Mua Thật",
            ["Command.Phone"] = "0912345678",
            ["Command.Email"] = "buyer@example.com",
            ["Command.Province"] = "TP. Hà Nội",
            ["Command.Ward"] = "Phường Hoàn Kiếm",
            ["Command.AddressLine"] = "1 Tràng Tiền",
            ["Command.PaymentMethod"] = "COD"
        });
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        var code = placed.Headers.Location!.ToString().Split('/').Last();
        var orderId = await DbAsync(db => db.Orders.Where(o => o.OrderCode == code).Select(o => o.Id).SingleAsync());
        return (buyer, orderId, productId, slug);
    }

    private async Task<HttpClient> RegisterAndReturnAsync()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);
        return client;
    }

    [GeneratedRegex("<form[^>]*method=\"post\"[^>]*>.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PostFormRegex();

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMetaRegex();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
