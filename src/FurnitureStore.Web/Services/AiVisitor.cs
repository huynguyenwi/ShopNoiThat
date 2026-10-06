using System.Text.RegularExpressions;
using FurnitureStore.Application.AI;
using FurnitureStore.Web.Infrastructure;

namespace FurnitureStore.Web.Services;

/// <summary>
/// Owner of AI conversations: the signed-in user, or a guest identified by a random HttpOnly cookie (a GUID, no personal
/// data). Guest conversations move to the account at sign-in.
/// </summary>
public static partial class AiVisitor
{
    public const string CookieName = ".NhaMoc.Ai";

    /// <param name="issueCookieWith">When given, a guest without a cookie receives one (only for write requests).</param>
    public static AiCaller Resolve(HttpContext context, TimeProvider? issueCookieWith = null)
    {
        var userId = context.User.UserId();
        if (userId is not null)
        {
            return new AiCaller(userId, null);
        }

        var anonymousId = AnonymousId(context);
        if (anonymousId is null && issueCookieWith is not null)
        {
            anonymousId = Guid.NewGuid().ToString("N");
            context.Response.Cookies.Append(CookieName, anonymousId, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Expires = issueCookieWith.GetUtcNow().AddDays(30)
            });
        }

        return new AiCaller(null, anonymousId);
    }

    public static string? AnonymousId(HttpContext context)
    {
        var value = context.Request.Cookies[CookieName];
        return value is not null && IdRegex().IsMatch(value) ? value : null;
    }

    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, SameSite = SameSiteMode.Lax });

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex IdRegex();
}
