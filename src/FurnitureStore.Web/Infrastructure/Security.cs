using System.Security.Claims;
using FurnitureStore.Infrastructure.Identity;

namespace FurnitureStore.Web.Infrastructure;

/// <summary>Authorization policy names used with [Authorize(Policy = ...)].</summary>
public static class AuthorizationPolicies
{
    /// <summary>ADMIN role only: dashboard, catalog, orders, users, settings.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>ADMIN or STAFF: customer chat, order handling.</summary>
    public const string BackOffice = "BackOffice";

    /// <summary>Any signed-in account.</summary>
    public const string SignedIn = "SignedIn";
}

/// <summary>Rate limiter policy names.</summary>
public static class RateLimitPolicies
{
    /// <summary>Login, register, forgot password: per client IP.</summary>
    public const string Authentication = "auth";

    /// <summary>Contact form and review submissions: per client IP.</summary>
    public const string Forms = "forms";

    /// <summary>AI assistant calls: per signed-in user, or per IP for guests (AI:RequestsPerMinute).</summary>
    public const string Ai = "ai";
}

/// <summary>Bound from the "RateLimiting" section.</summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";

    public int AuthenticationPermitsPerMinute { get; set; } = 10;

    public int FormPermitsPerMinute { get; set; } = 5;

    /// <summary>Global safety net for /api/*: requests per minute per client IP.</summary>
    public int ApiPermitsPerMinute { get; set; } = 300;
}

public static class ClaimsPrincipalExtensions
{
    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(AppClaimTypes.FullName) ?? user.Identity?.Name ?? string.Empty;

    public static string? AvatarUrl(this ClaimsPrincipal user) => user.FindFirstValue(AppClaimTypes.AvatarUrl);

    public static string? UserId(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);
}

public static class SecurityHeadersExtensions
{
    /// <summary>Security headers for every response, including a strict Content-Security-Policy.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "SAMEORIGIN";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            // Endpoints with their own, stricter policy (e.g. the placeholder SVG images) keep it.
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                headers.ContentSecurityPolicy = ContentSecurityPolicy.For(context.Request);
            }

            return Task.CompletedTask;
        });

        await next();
    });
}

public static class ContentSecurityPolicy
{
    /// <summary>
    /// No inline or third-party scripts (all scripts are files under /js and /lib); inline style attributes are allowed.
    /// External resources: Google Fonts and the Google Maps embed of the contact page. WebSocket only to this host (SignalR chat).
    /// </summary>
    public static string For(HttpRequest request)
    {
        var socket = (request.IsHttps ? "wss://" : "ws://") + request.Host.Value;
        var policy = "default-src 'self'; "
                     + "script-src 'self'; "
                     + "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; "
                     + "font-src 'self' https://fonts.gstatic.com data:; "
                     + "img-src 'self' data: blob:; "
                     + $"connect-src 'self' {socket}; "
                     + "frame-src https://www.google.com https://maps.google.com; "
                     + "frame-ancestors 'self'; "
                     + "object-src 'none'; "
                     + "base-uri 'self'; "
                     + "form-action 'self'; "
                     + "manifest-src 'self'";
        return request.IsHttps ? policy + "; upgrade-insecure-requests" : policy;
    }
}
