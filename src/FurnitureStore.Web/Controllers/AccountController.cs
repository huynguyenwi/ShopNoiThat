using System.Text;
using FurnitureStore.Application.AI;
using FurnitureStore.Application.Chat;
using FurnitureStore.Application.Common.Emails;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Identity;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.Services;
using FurnitureStore.Web.ViewModels.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Web.Controllers;

[Authorize]
public sealed class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IEmailSender emailSender,
    IAuditLogService auditLog,
    IFileStorageService fileStorage,
    IStoreInfoService storeInfo,
    ICartService cartService,
    CartOwnerResolver cartOwners,
    IChatService chatService,
    IAssistantService assistant,
    TimeProvider timeProvider,
    ILogger<AccountController> logger) : Controller
{
    private const string InvalidLoginMessage = "Email hoặc mật khẩu không đúng.";
    public const string StatusMessageKey = "StatusMessage";

    // ------------------------------------------------------------------ Login / Logout

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        ViewData["Robots"] = "noindex,nofollow";
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Authentication)]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["Robots"] = "noindex,nofollow";
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is null)
        {
            await LogFailedLoginAsync(null, model.Email, "Tài khoản không tồn tại");
            ModelState.AddModelError(string.Empty, InvalidLoginMessage);
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            user.LastLoginAt = timeProvider.GetUtcNow().UtcDateTime;
            await userManager.UpdateAsync(user);
            await MergeGuestCartAsync(user.Id);
            await ClaimGuestChatAsync(user);
            await auditLog.LogAsync(new AuditEntry(AuditAction.Login, "User", user.Id, "Đăng nhập thành công", UserId: user.Id, UserName: user.Email));
            logger.LogInformation("User {UserId} signed in", user.Id);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return LocalRedirect(model.ReturnUrl);
            }

            return await userManager.IsInRoleAsync(user, AppRoles.Admin) ? Redirect("/admin") : Redirect("/");
        }

        if (result.IsLockedOut)
        {
            logger.LogWarning("User {UserId} is locked out", user.Id);
            ModelState.AddModelError(string.Empty, "Tài khoản tạm thời bị khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút hoặc đặt lại mật khẩu.");
            await LogFailedLoginAsync(user.Id, user.Email, "Tài khoản đang bị khóa");
        }
        else if (result.IsNotAllowed)
        {
            ModelState.AddModelError(string.Empty, "Tài khoản đã bị vô hiệu hóa. Vui lòng liên hệ cửa hàng để được hỗ trợ.");
            await LogFailedLoginAsync(user.Id, user.Email, "Tài khoản bị vô hiệu hóa");
        }
        else
        {
            ModelState.AddModelError(string.Empty, InvalidLoginMessage);
            await LogFailedLoginAsync(user.Id, user.Email, "Sai mật khẩu");
        }

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        var userId = User.UserId();
        await signInManager.SignOutAsync();
        await auditLog.LogAsync(new AuditEntry(AuditAction.Logout, "User", userId, "Đăng xuất", UserId: userId, UserName: User.Identity?.Name));
        logger.LogInformation("User {UserId} signed out", userId);
        return Redirect("/");
    }

    [HttpGet("account/access-denied"), AllowAnonymous]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        ViewData["Robots"] = "noindex,nofollow";
        return View();
    }

    // ------------------------------------------------------------------ Register

    [HttpGet, AllowAnonymous]
    public IActionResult Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        return View(new RegisterViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Authentication)]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = model.FullName.Trim(),
            PhoneNumber = model.PhoneNumber.Trim(),
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        await userManager.AddToRoleAsync(user, AppRoles.User);
        await signInManager.SignInAsync(user, isPersistent: false);
        await MergeGuestCartAsync(user.Id);
        await ClaimGuestChatAsync(user);

        logger.LogInformation("New customer account {UserId} registered", user.Id);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Create, "User", user.Id, "Đăng ký tài khoản",
            NewValues: new { user.Email, user.FullName, user.PhoneNumber }, UserId: user.Id, UserName: user.Email));

        var store = await storeInfo.GetAsync(HttpContext.RequestAborted);
        await TrySendEmailAsync(new EmailMessage(email, $"Chào mừng bạn đến với {store.Name}",
            EmailTemplates.Welcome(store.Name, user.FullName, AbsoluteUrl("/products")), user.FullName));

        TempData[StatusMessageKey] = $"Đăng ký thành công! Chào mừng bạn đến với {store.BrandName}.";
        return RedirectToLocal(model.ReturnUrl);
    }

    // ------------------------------------------------------------------ Forgot / reset password

    [HttpGet, AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost, AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Authentication)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is not null && user.IsActive)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var resetUrl = AbsoluteUrl(Url.Action(nameof(ResetPassword), "Account", new { email = user.Email, code })!);

            await TrySendEmailAsync(new EmailMessage(user.Email!, "Đặt lại mật khẩu",
                EmailTemplates.PasswordReset((await storeInfo.GetAsync(HttpContext.RequestAborted)).Name, user.FullName, resetUrl,
                    (int)AccountSecurity.PasswordResetTokenLifespan.TotalHours), user.FullName));
            logger.LogInformation("Password reset requested for user {UserId}", user.Id);
        }

        // Same response whether or not the email exists, so accounts cannot be enumerated.
        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult ForgotPasswordConfirmation() => View();

    [HttpGet, AllowAnonymous]
    public IActionResult ResetPassword(string? email, string? code)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(code))
        {
            return BadRequest();
        }

        ViewData["Robots"] = "noindex,nofollow";
        return View(new ResetPasswordViewModel { Email = email, Code = code });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Authentication)]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Code));
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "Liên kết đặt lại mật khẩu không hợp lệ.");
            return View(model);
        }

        var result = await userManager.ResetPasswordAsync(user, token, model.Password);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        // A successful reset also lifts a lockout caused by failed login attempts.
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);

        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, "User", user.Id, "Đặt lại mật khẩu qua email", UserId: user.Id, UserName: user.Email));
        logger.LogInformation("Password reset completed for user {UserId}", user.Id);
        return RedirectToAction(nameof(ResetPasswordConfirmation));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult ResetPasswordConfirmation() => View();

    // ------------------------------------------------------------------ Profile

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await GetCurrentUserAsync();
        return View(ToProfileViewModel(user));
    }

    [HttpPost]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        var user = await GetCurrentUserAsync();
        if (!ModelState.IsValid)
        {
            model.Email = user.Email ?? string.Empty;
            model.AvatarUrl = user.AvatarUrl;
            model.MemberSince = user.CreatedAt;
            return View(model);
        }

        var before = new { user.FullName, user.PhoneNumber, user.Gender, user.DateOfBirth };
        user.FullName = model.FullName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
        user.Gender = model.Gender;
        user.DateOfBirth = model.DateOfBirth;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(ToProfileViewModel(user));
        }

        await signInManager.RefreshSignInAsync(user);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, "User", user.Id, "Cập nhật hồ sơ",
            OldValues: before, NewValues: new { user.FullName, user.PhoneNumber, user.Gender, user.DateOfBirth }));

        TempData[StatusMessageKey] = "Đã cập nhật thông tin cá nhân.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    [RequestSizeLimit(UploadLimits.PerImageBytes + UploadLimits.FormFieldsBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.PerImageBytes + UploadLimits.FormFieldsBytes)]
    public async Task<IActionResult> Avatar(IFormFile? avatar, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (avatar is null || avatar.Length == 0)
        {
            TempData[StatusMessageKey] = "Lỗi: Vui lòng chọn ảnh đại diện.";
            return RedirectToAction(nameof(Profile));
        }

        try
        {
            await using var stream = avatar.OpenReadStream();
            var stored = await fileStorage.SaveImageAsync(stream, avatar.FileName, Application.Common.Media.ImagePreset.Avatar, cancellationToken);

            var oldAvatar = user.AvatarUrl;
            user.AvatarUrl = stored.Url;
            await userManager.UpdateAsync(user);
            await fileStorage.DeleteAsync(oldAvatar, cancellationToken);
            await signInManager.RefreshSignInAsync(user);

            TempData[StatusMessageKey] = "Đã cập nhật ảnh đại diện.";
        }
        catch (AppValidationException ex)
        {
            TempData[StatusMessageKey] = "Lỗi: " + string.Join(" ", ex.Errors);
        }

        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await GetCurrentUserAsync();
        var result = await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        // ChangePasswordAsync rotates the security stamp: other sessions are signed out, this one is refreshed.
        await signInManager.RefreshSignInAsync(user);
        await auditLog.LogAsync(new AuditEntry(AuditAction.Update, "User", user.Id, "Đổi mật khẩu"));
        await TrySendEmailAsync(new EmailMessage(user.Email!, "Mật khẩu của bạn đã được thay đổi",
            EmailTemplates.PasswordChanged((await storeInfo.GetAsync(HttpContext.RequestAborted)).Name, user.FullName), user.FullName));

        TempData[StatusMessageKey] = "Đổi mật khẩu thành công.";
        return RedirectToAction(nameof(Profile));
    }

    // ------------------------------------------------------------------ Helpers

    private async Task<ApplicationUser> GetCurrentUserAsync() =>
        await userManager.GetUserAsync(User) ?? throw new UnauthorizedAccessException();

    private static ProfileViewModel ToProfileViewModel(ApplicationUser user) => new()
    {
        FullName = user.FullName,
        Email = user.Email ?? string.Empty,
        PhoneNumber = user.PhoneNumber,
        Gender = user.Gender,
        DateOfBirth = user.DateOfBirth,
        AvatarUrl = user.AvatarUrl,
        MemberSince = user.CreatedAt
    };

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : Redirect("/");

    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }

    /// <summary>Items added before signing in are kept: the anonymous cart is merged into the account's cart.</summary>
    private async Task MergeGuestCartAsync(string userId)
    {
        var anonymousId = cartOwners.AnonymousId;
        if (anonymousId is null)
        {
            return;
        }

        try
        {
            await cartService.MergeAsync(anonymousId, userId, HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to merge guest cart for user {UserId}", userId);
        }

        cartOwners.ClearAnonymousCookie();
    }

    /// <summary>
    /// A shop chat and AI conversations started as a guest continue in the account
    /// (the shop chat only when the account has no conversation yet).
    /// </summary>
    private async Task ClaimGuestChatAsync(ApplicationUser user)
    {
        var guestKey = ChatIdentity.GuestKey(HttpContext);
        if (guestKey is not null)
        {
            try
            {
                await chatService.ClaimGuestConversationAsync(guestKey, user.Id, user.FullName, HttpContext.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to link guest chat for user {UserId}", user.Id);
            }

            ChatIdentity.ClearGuestKey(HttpContext);
        }

        // Conversations with the AI assistant started as a guest move to the account too.
        var anonymousAiId = AiVisitor.AnonymousId(HttpContext);
        if (anonymousAiId is not null)
        {
            try
            {
                await assistant.ClaimAnonymousConversationsAsync(anonymousAiId, user.Id, HttpContext.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to link guest AI conversations for user {UserId}", user.Id);
            }

            AiVisitor.Clear(HttpContext);
        }
    }

    private string AbsoluteUrl(string relative) => $"{Request.Scheme}://{Request.Host}{Request.PathBase}{relative}";

    private Task LogFailedLoginAsync(string? userId, string? email, string reason)
    {
        logger.LogWarning("Failed sign-in attempt ({Reason}) for user {UserId}", reason, userId ?? "(unknown)");
        return auditLog.LogAsync(new AuditEntry(AuditAction.LoginFailed, "User", userId, $"Đăng nhập thất bại: {reason}", UserId: userId, UserName: email));
    }

    /// <summary>Email problems must not break registration or password reset; they are logged instead.</summary>
    private async Task TrySendEmailAsync(EmailMessage message)
    {
        try
        {
            await emailSender.SendAsync(message, HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send email \"{Subject}\"", message.Subject);
        }
    }
}
