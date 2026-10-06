using System.Security.Claims;
using System.Text.RegularExpressions;
using FurnitureStore.Application.Chat;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Web.Infrastructure;

namespace FurnitureStore.Web.Services;

/// <summary>
/// Who is chatting: the signed-in user, or a guest identified by a random, HttpOnly cookie (a GUID, no personal data).
/// Works from controllers and from the SignalR hub (which only has the connection's HttpContext).
/// </summary>
public static partial class ChatIdentity
{
    public const string CookieName = ".NhaMoc.Chat";

    public static ChatCustomer Resolve(HttpContext context)
    {
        var user = context.User;
        var userId = user.UserId();
        if (userId is not null)
        {
            return new ChatCustomer(userId, null, user.DisplayName(), user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name);
        }

        return new ChatCustomer(null, GuestKey(context));
    }

    /// <summary>The guest key from the cookie, when it is well-formed.</summary>
    public static string? GuestKey(HttpContext context)
    {
        var value = context.Request.Cookies[CookieName];
        return value is not null && GuestKeyRegex().IsMatch(value) ? value : null;
    }

    /// <summary>Returns the guest's chat identity, issuing the cookie on first use.</summary>
    public static ChatCustomer EnsureGuest(HttpContext context, TimeProvider timeProvider)
    {
        var key = GuestKey(context);
        if (key is null)
        {
            key = Guid.NewGuid().ToString("N");
            context.Response.Cookies.Append(CookieName, key, new CookieOptions
            {
                HttpOnly = true,
                Secure = CookieSecurity.IsSecure(context),
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Expires = timeProvider.GetUtcNow().AddDays(30)
            });
        }

        return new ChatCustomer(null, key);
    }

    public static void ClearGuestKey(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = CookieSecurity.IsSecure(context), SameSite = SameSiteMode.Lax });

    public static bool IsStaff(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && (user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Staff));

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex GuestKeyRegex();
}
