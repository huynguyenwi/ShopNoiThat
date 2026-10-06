using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Infrastructure.Identity;

public static class AccountSecurity
{
    /// <summary>Password reset links stay valid for this long.</summary>
    public static readonly TimeSpan PasswordResetTokenLifespan = TimeSpan.FromHours(2);

    public const int MaxFailedAccessAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}

/// <summary>Custom claim types added to the authentication cookie.</summary>
public static class AppClaimTypes
{
    public const string FullName = "full_name";
    public const string AvatarUrl = "avatar_url";
}

/// <summary>Blocks sign-in for accounts deactivated by an admin (in addition to Identity lockout).</summary>
public sealed class ApplicationSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ApplicationUser user) =>
        user.IsActive && await base.CanSignInAsync(user);
}

/// <summary>Adds display name and avatar to the cookie so the header can show them without a database query.</summary>
public sealed class ApplicationClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AppClaimTypes.FullName, string.IsNullOrWhiteSpace(user.FullName) ? user.Email ?? string.Empty : user.FullName));
        if (!string.IsNullOrEmpty(user.AvatarUrl))
        {
            identity.AddClaim(new Claim(AppClaimTypes.AvatarUrl, user.AvatarUrl));
        }

        return identity;
    }
}

/// <summary>Vietnamese messages for ASP.NET Core Identity validation errors.</summary>
public sealed class VietnameseIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Error(nameof(DefaultError), "Đã có lỗi xảy ra.");
    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), "Dữ liệu đã bị thay đổi, vui lòng thử lại.");
    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "Mật khẩu không đúng.");
    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "Liên kết không hợp lệ hoặc đã hết hạn.");
    public override IdentityError LoginAlreadyAssociated() => Error(nameof(LoginAlreadyAssociated), "Tài khoản đăng nhập này đã được liên kết.");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), "Tên đăng nhập không hợp lệ.");
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), "Email không hợp lệ.");
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), "Email này đã được sử dụng.");
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), "Email này đã được sử dụng.");
    public override IdentityError InvalidRoleName(string? role) => Error(nameof(InvalidRoleName), "Tên vai trò không hợp lệ.");
    public override IdentityError DuplicateRoleName(string role) => Error(nameof(DuplicateRoleName), "Vai trò đã tồn tại.");
    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), "Tài khoản đã có mật khẩu.");
    public override IdentityError UserLockoutNotEnabled() => Error(nameof(UserLockoutNotEnabled), "Tài khoản không hỗ trợ khóa.");
    public override IdentityError UserAlreadyInRole(string role) => Error(nameof(UserAlreadyInRole), "Người dùng đã có vai trò này.");
    public override IdentityError UserNotInRole(string role) => Error(nameof(UserNotInRole), "Người dùng không có vai trò này.");
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"Mật khẩu phải có ít nhất {length} ký tự.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), $"Mật khẩu phải có ít nhất {uniqueChars} ký tự khác nhau.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "Mật khẩu phải có ít nhất một ký tự đặc biệt.");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "Mật khẩu phải có ít nhất một chữ số (0-9).");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "Mật khẩu phải có ít nhất một chữ thường (a-z).");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "Mật khẩu phải có ít nhất một chữ hoa (A-Z).");
    public override IdentityError RecoveryCodeRedemptionFailed() => Error(nameof(RecoveryCodeRedemptionFailed), "Mã khôi phục không hợp lệ.");

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
