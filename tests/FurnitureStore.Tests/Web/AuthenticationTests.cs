using System.Net;
using System.Text.RegularExpressions;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

public sealed partial class AuthenticationTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private const string Admin = FurnitureStoreWebApplicationFactory.AdminEmail;
    private const string AdminPassword = FurnitureStoreWebApplicationFactory.AdminPassword;
    private const string Customer = FurnitureStoreWebApplicationFactory.UserEmail;
    private const string CustomerPassword = FurnitureStoreWebApplicationFactory.UserPassword;

    [Fact]
    public async Task Register_TermsCheckbox_UsesARuleBrowsersCanEvaluate_AndIsEnforcedOnTheServer()
    {
        // Regression: [Range(typeof(bool), "true", "true")] rendered data-val-range, which jQuery Validate always
        // rejected on a checkbox - registration was impossible in a real browser.
        var client = factory.CreateClient();
        var page = await client.GetStringAsync("/account/register");
        var checkbox = Regex.Match(page, "<input[^>]*id=\"AcceptTerms\"[^>]*>").Value;
        Assert.Contains("data-val-mustbetrue=\"Bạn cần đồng ý với điều khoản sử dụng.\"", checkbox);
        Assert.DoesNotContain("data-val-range", checkbox);
        Assert.Contains("/js/validation-extensions.js", page);
        Assert.Contains("addBool('mustbetrue')", await client.GetStringAsync("/js/validation-extensions.js"));

        var response = await PostFormAsync(client, "/account/register", "/account/register", new Dictionary<string, string>
        {
            ["FullName"] = "Không Đồng Ý",
            ["Email"] = $"noterms-{Guid.NewGuid():N}@example.com",
            ["PhoneNumber"] = "0912345678",
            ["Password"] = "Khach@Hang123",
            ["ConfirmPassword"] = "Khach@Hang123",
            ["AcceptTerms"] = "false"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bạn cần đồng ý với điều khoản sử dụng.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LoginPage_RendersFormWithAntiforgeryToken()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/account/login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("name=\"Password\"", html);
    }

    [Fact]
    public async Task Login_AsAdmin_RedirectsToAdminDashboard()
    {
        var client = factory.CreateClient();

        var response = await LoginAsync(client, Admin, AdminPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".NhaMoc.Auth=") && c.Contains("secure") && c.Contains("httponly"));
    }

    [Fact]
    public async Task Login_AsCustomer_RedirectsHome_AndHeaderShowsName()
    {
        var client = factory.CreateClient();

        var response = await LoginAsync(client, Customer, CustomerPassword);
        var home = await client.GetStringAsync("/");

        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains("Khách hàng Demo", home);
        Assert.Contains("Đăng xuất", home);
        Assert.DoesNotContain("Trang quản trị", home);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShowsGenericError()
    {
        var client = factory.CreateClient();

        var response = await LoginAsync(client, Customer, "Wrong@Password1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Email hoặc mật khẩu không đúng.", html);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ShowsSameGenericError()
    {
        var client = factory.CreateClient();

        var response = await LoginAsync(client, "nobody@example.com", "Whatever@123");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Email hoặc mật khẩu không đúng.", html);
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryToken_IsRejected()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = Admin,
            ["Password"] = AdminPassword
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_IgnoresExternalReturnUrl()
    {
        var client = factory.CreateClient();

        var response = await LoginAsync(client, Customer, CustomerPassword, returnUrl: "https://evil.example.com/steal");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_HonoursLocalReturnUrl()
    {
        var client = factory.CreateClient();

        var response = await LoginAsync(client, Customer, CustomerPassword, returnUrl: "/account/profile");

        Assert.Equal("/account/profile", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Account_IsLockedOut_AfterFiveFailedAttempts()
    {
        var client = factory.CreateClient();
        var email = await RegisterAsync(client, password: "Correct@Pass1");
        var anonymous = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(anonymous, email, "Wrong@Pass1");
        }

        var response = await LoginAsync(anonymous, email, "Correct@Pass1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("tạm thời bị khóa", html);
    }

    [Fact]
    public async Task Register_CreatesCustomerWithUserRole_SignsIn_AndSendsWelcomeEmail()
    {
        var client = factory.CreateClient();

        var email = await RegisterAsync(client);

        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(await userManager.IsInRoleAsync(user, AppRoles.User));
        Assert.False(await userManager.IsInRoleAsync(user, AppRoles.Admin));
        Assert.Equal("0912345678", user.PhoneNumber);

        var profile = await client.GetAsync("/account/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);

        Assert.Contains(Directory.GetFiles(factory.EmailPickupDirectory), file => File.ReadAllText(file).Contains($"To: Khách Thử Nghiệm <{email}>"));
    }

    [Fact]
    public async Task Register_WithExistingEmail_ShowsVietnameseError()
    {
        var client = factory.CreateClient();

        var response = await PostFormAsync(client, "/account/register", "/account/register", new Dictionary<string, string>
        {
            ["FullName"] = "Trùng Email",
            ["Email"] = Customer,
            ["PhoneNumber"] = "0912345678",
            ["Password"] = "Valid@Pass1",
            ["ConfirmPassword"] = "Valid@Pass1",
            ["AcceptTerms"] = "true"
        });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Email này đã được sử dụng.", html);
    }

    [Theory]
    [InlineData("12345", "Valid@Pass1", "Valid@Pass1", "true", "Số điện thoại không hợp lệ")]
    [InlineData("0912345678", "short", "short", "true", "ít nhất 8 ký tự")]
    [InlineData("0912345678", "Valid@Pass1", "Other@Pass1", "true", "không khớp")]
    [InlineData("0912345678", "Valid@Pass1", "Valid@Pass1", "false", "đồng ý với điều khoản")]
    [InlineData("0912345678", "alllowercase1", "alllowercase1", "true", "chữ hoa")]
    public async Task Register_ValidatesInputOnServer(string phone, string password, string confirm, string accept, string expectedError)
    {
        var client = factory.CreateClient();

        var response = await PostFormAsync(client, "/account/register", "/account/register", new Dictionary<string, string>
        {
            ["FullName"] = "Kiểm Tra",
            ["Email"] = $"v-{Guid.NewGuid():N}@example.com",
            ["PhoneNumber"] = phone,
            ["Password"] = password,
            ["ConfirmPassword"] = confirm,
            ["AcceptTerms"] = accept
        });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedError, html);
    }

    [Fact]
    public async Task ForgotAndResetPassword_FullFlow_ThroughEmailLink()
    {
        var client = factory.CreateClient();
        var email = await RegisterAsync(client, password: "Before@Reset1");
        var anonymous = factory.CreateClient();

        var forgot = await PostFormAsync(anonymous, "/account/forgotpassword", "/account/forgotpassword",
            new Dictionary<string, string> { ["Email"] = email });
        Assert.Equal(HttpStatusCode.Redirect, forgot.StatusCode);

        var emailFile = Directory.GetFiles(factory.EmailPickupDirectory)
            .Select(File.ReadAllText)
            .Single(content => content.Contains($"<{email}>") && content.Contains("Subject: Đặt lại mật khẩu"));
        var link = WebUtility.HtmlDecode(ResetLinkRegex().Match(emailFile).Groups[1].Value);
        var resetPath = new Uri(link).PathAndQuery;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);

        var reset = await PostFormAsync(anonymous, resetPath, "/account/resetpassword", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Code"] = query["code"]!,
            ["Password"] = "After@Reset1",
            ["ConfirmPassword"] = "After@Reset1"
        });
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);

        var oldLogin = await LoginAsync(factory.CreateClient(), email, "Before@Reset1");
        var newLogin = await LoginAsync(factory.CreateClient(), email, "After@Reset1");
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, newLogin.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_ForUnknownEmail_LooksIdentical()
    {
        var client = factory.CreateClient();

        var response = await PostFormAsync(client, "/account/forgotpassword", "/account/forgotpassword",
            new Dictionary<string, string> { ["Email"] = "unknown-person@example.com" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/ForgotPasswordConfirmation", response.Headers.Location?.OriginalString, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetPassword_WithForgedCode_Fails()
    {
        var client = factory.CreateClient();
        const string forgedCode = "Zm9yZ2VkLXRva2Vu"; // base64url("forged-token")

        var response = await PostFormAsync(client, $"/account/resetpassword?email={Customer}&code={forgedCode}", "/account/resetpassword",
            new Dictionary<string, string>
            {
                ["Email"] = Customer,
                ["Code"] = forgedCode,
                ["Password"] = "Hacked@Pass1",
                ["ConfirmPassword"] = "Hacked@Pass1"
            });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("không hợp lệ hoặc đã hết hạn", html);
    }

    [Fact]
    public async Task ChangePassword_ThenOldPasswordStopsWorking()
    {
        var client = factory.CreateClient();
        var email = await RegisterAsync(client, password: "Old@Password1");

        var response = await PostFormAsync(client, "/account/changepassword", "/account/changepassword", new Dictionary<string, string>
        {
            ["CurrentPassword"] = "Old@Password1",
            ["NewPassword"] = "New@Password1",
            ["ConfirmPassword"] = "New@Password1"
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(factory.CreateClient(), email, "Old@Password1")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(factory.CreateClient(), email, "New@Password1")).StatusCode);
    }

    [Fact]
    public async Task Logout_EndsSession()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/account/profile")).StatusCode);

        var logout = await PostFormAsync(client, "/account/profile", "/account/logout", new Dictionary<string, string>());
        var profile = await client.GetAsync("/account/profile");

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, profile.StatusCode);
        Assert.StartsWith("https://localhost/account/login", profile.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Profile_Update_ChangesNameShownInHeader()
    {
        var client = factory.CreateClient();
        await RegisterAsync(client);

        var response = await PostFormAsync(client, "/account/profile", "/account/profile", new Dictionary<string, string>
        {
            ["FullName"] = "Tên Mới Cập Nhật",
            ["PhoneNumber"] = "0987654321",
            ["Gender"] = "Nữ",
            ["DateOfBirth"] = "1995-05-20"
        });
        var home = await client.GetStringAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Tên Mới Cập Nhật", home);
    }

    [GeneratedRegex("href=\"([^\"]+/account/resetpassword[^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ResetLinkRegex();
}
