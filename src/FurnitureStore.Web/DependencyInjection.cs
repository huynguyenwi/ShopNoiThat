using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using FurnitureStore.Application.Chat;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Web.Hubs;
using FurnitureStore.Web.Infrastructure;
using FurnitureStore.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders;

namespace FurnitureStore.Web;

public static class DependencyInjection
{
    /// <summary>Header name used by site.js to send the anti-forgery token on AJAX requests.</summary>
    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";

    public static IServiceCollection AddWebServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<ApplicationSettings>()
            .Bind(configuration.GetSection(ApplicationSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SeoSettings>().Bind(configuration.GetSection(SeoSettings.SectionName));

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<CartOwnerResolver>();

        services.AddRouting(options =>
        {
            options.LowercaseUrls = true;
            options.LowercaseQueryStrings = false;
        });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = ".NhaMoc.Antiforgery";
            options.Cookie.SecurePolicy = CookieSecurity.PolicyFor(environment);
        });

        // Output Vietnamese text as-is instead of &#xNN; entities / \uNNNN escapes.
        // HTML-sensitive characters (< > & " ') are still encoded, so XSS protection is unchanged.
        services.Configure<WebEncoderOptions>(options =>
            options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
        var jsonEncoder = JavaScriptEncoder.Create(UnicodeRanges.All);

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Encoder = jsonEncoder;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services
            .AddControllersWithViews(options =>
            {
                // Every POST/PUT/PATCH/DELETE must carry a valid anti-forgery token (form field or X-CSRF-TOKEN header).
                options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
                options.Filters.Add(new AntiforgeryFailureFilter());
                options.Filters.Add(new PayloadTooLargeFilter());

                // Vietnamese texts for model binding errors and attributes declared without a message.
                ValidationMessages.Configure(options.ModelBindingMessageProvider);
                options.ModelMetadataDetailsProviders.Add(new DefaultValidationMessagesProvider());

                // Business validation lives in FluentValidation (Vietnamese messages). Without this, every non-nullable
                // string would also get MVC's implicit English "The X field is required." error, shown before ours.
                // View models that need "required" declare [Required(ErrorMessage = "...")] explicitly.
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Encoder = jsonEncoder;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.AllowInputFormatterExceptionMessages = false; // no parser / CLR type details in responses
            });
        services.AddSingleton<IValidationAttributeAdapterProvider, LocalizedValidationAttributeAdapterProvider>();

        // API controllers return the standard ApiResponse envelope for model-validation errors too.
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = ApiValidation.CreateInvalidModelStateResponse;

            // Bodiless 4xx results (415, NotFound()...) stay empty so ApiStatusCodePages writes the envelope
            // instead of a ProblemDetails document.
            options.SuppressMapClientErrors = true;
        });

        // Realtime chat. Payloads use the same JSON settings as the API (camelCase, enums as strings, Vietnamese unescaped).
        services.AddSignalR(options =>
            {
                options.MaximumReceiveMessageSize = 16 * 1024;
                options.EnableDetailedErrors = false; // only HubException messages (written for users) reach the browser
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.Encoder = jsonEncoder;
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
        services.AddScoped<IChatNotifier, SignalRChatNotifier>();

        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddHealthChecks();

        // Performance: Brotli / gzip for HTML, JSON, CSS, JS, SVG and XML. Safe over HTTPS here because anti-forgery
        // request tokens are different on every response (BREACH needs a secret repeated verbatim).
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
            options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
            options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes
                .Concat(["image/svg+xml", "application/xml", "text/plain"]);
        });

        // Access log without query strings (they may carry reset-password tokens) and without bodies.
        services.AddHttpLogging(options =>
        {
            options.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestMethod
                                    | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestPath
                                    | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponseStatusCode
                                    | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.Duration;
            options.CombineLogs = true;
        });

        AddAuthentication(services, environment);
        AddRateLimiting(services, configuration);

        return services;
    }

    private static void AddAuthentication(IServiceCollection services, IHostEnvironment environment)
    {
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = ".NhaMoc.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurity.PolicyFor(environment);
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.LoginPath = "/account/login";
            options.LogoutPath = "/account/logout";
            options.AccessDeniedPath = "/account/access-denied";
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;

            // API / AJAX callers get 401/403 (rendered as JSON by the status code pages) instead of an HTML redirect.
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.HttpContext.IsApiRequest())
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.HttpContext.IsApiRequest())
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.AdminOnly, policy => policy.RequireAuthenticatedUser().RequireRole(AppRoles.Admin))
            .AddPolicy(AuthorizationPolicies.BackOffice, policy => policy.RequireAuthenticatedUser().RequireRole(AppRoles.Admin, AppRoles.Staff))
            .AddPolicy(AuthorizationPolicies.SignedIn, policy => policy.RequireAuthenticatedUser());
    }

    private static void AddRateLimiting(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitSettings>().Bind(configuration.GetSection(RateLimitSettings.SectionName));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Safety net for every API call (the endpoint policies below are stricter for sensitive actions).
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (!httpContext.Request.Path.StartsWithSegments("/api"))
                {
                    return RateLimitPartition.GetNoLimiter("not-api");
                }

                var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    "api:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.ApiPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });

            options.AddPolicy(RateLimitPolicies.Authentication, httpContext =>
            {
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.AuthenticationPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
            options.AddPolicy(RateLimitPolicies.Ai, httpContext =>
            {
                var ai = httpContext.RequestServices.GetRequiredService<IOptionsMonitor<AiSettings>>().CurrentValue;
                var key = httpContext.User.UserId() ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    "ai:" + key,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = ai.RequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
            options.AddPolicy(RateLimitPolicies.Forms, httpContext =>
            {
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    "forms:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.FormPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
        });
    }
}
