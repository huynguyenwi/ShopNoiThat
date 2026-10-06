namespace FurnitureStore.Web.Infrastructure;

/// <summary>
/// Cookies are always "Secure" (HTTPS only) outside Development. In Development they follow the request, so the site
/// also works over plain HTTP: Visual Studio's "http" profile, or a phone on the LAN testing QR codes.
/// </summary>
public static class CookieSecurity
{
    public static CookieSecurePolicy PolicyFor(IHostEnvironment environment) =>
        environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

    /// <summary>The Secure flag of a cookie written by hand (cart, chat, AI visitor).</summary>
    public static bool IsSecure(HttpContext context) =>
        context.Request.IsHttps || !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
}
