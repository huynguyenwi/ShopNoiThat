using System.Text.RegularExpressions;
using FurnitureStore.Application.Sales;
using FurnitureStore.Web.Infrastructure;

namespace FurnitureStore.Web.Services;

/// <summary>
/// Identifies the cart of the current visitor: the signed-in user, or an anonymous visitor through a random,
/// HttpOnly cookie (only a GUID - no personal data). The anonymous cart is merged into the user's cart at sign-in.
/// </summary>
public sealed partial class CartOwnerResolver(IHttpContextAccessor accessor, TimeProvider timeProvider)
{
    public const string CookieName = ".NhaMoc.Cart";

    private HttpContext Context => accessor.HttpContext ?? throw new InvalidOperationException("No active HTTP request.");

    /// <param name="createIfMissing">Issue an anonymous cart cookie when the visitor has none (only for write operations).</param>
    public CartOwner Resolve(bool createIfMissing = false)
    {
        var userId = Context.User.UserId();
        if (userId is not null)
        {
            return new CartOwner(userId, null);
        }

        var anonymousId = AnonymousId;
        if (anonymousId is null && createIfMissing)
        {
            anonymousId = Guid.NewGuid().ToString("N");
            Context.Response.Cookies.Append(CookieName, anonymousId, new CookieOptions
            {
                HttpOnly = true,
                Secure = CookieSecurity.IsSecure(Context),
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Expires = timeProvider.GetUtcNow().AddDays(30)
            });
        }

        return new CartOwner(null, anonymousId);
    }

    /// <summary>The anonymous cart id from the cookie, when it is well-formed.</summary>
    public string? AnonymousId
    {
        get
        {
            var value = Context.Request.Cookies[CookieName];
            return value is not null && CartIdRegex().IsMatch(value) ? value : null;
        }
    }

    public void ClearAnonymousCookie() => Context.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = CookieSecurity.IsSecure(Context), SameSite = SameSiteMode.Lax });

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex CartIdRegex();
}
