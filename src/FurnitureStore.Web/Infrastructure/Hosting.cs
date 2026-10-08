using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;

namespace FurnitureStore.Web.Infrastructure;

public static class HostingExtensions
{
    /// <summary>
    /// Persists the data-protection keys (auth / anti-forgery cookies, reset tokens) so users stay signed in across
    /// restarts and several server instances can share them. Path: DataProtection:KeysPath (default App_Data/keys,
    /// excluded from git). On Windows the key files are additionally encrypted with DPAPI (DataProtection:Dpapi):
    /// "CurrentUser" (default), "LocalMachine" for shared IIS hosting whose application pool loads no user profile
    /// (e.g. Somee: user-scoped DPAPI fails there as soon as a form or login needs a key), or "None".
    /// </summary>
    public static IServiceCollection AddKeyStorage(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["DataProtection:KeysPath"];
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "keys")
            : Path.GetFullPath(configured, environment.ContentRootPath);

        var builder = services.AddDataProtection()
            .SetApplicationName("NhaMocFurniture")
            .PersistKeysToFileSystem(new DirectoryInfo(path));

        var dpapi = configuration["DataProtection:Dpapi"];
        if (OperatingSystem.IsWindows() && !string.Equals(dpapi, "None", StringComparison.OrdinalIgnoreCase))
        {
            builder.ProtectKeysWithDpapi(protectToLocalMachine: string.Equals(dpapi, "LocalMachine", StringComparison.OrdinalIgnoreCase));
        }

        return services;
    }

    /// <summary>
    /// Behind nginx / a load balancer the client IP (rate limits, logs) comes from X-Forwarded-For - trusted only from
    /// the proxies listed in ReverseProxy:KnownProxies. IIS in-process hosting needs nothing. Returns true when enabled.
    /// </summary>
    public static bool AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        var addresses = proxies.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => IPAddress.Parse(p.Trim())).ToList();
        if (addresses.Count == 0)
        {
            return false;
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var address in addresses)
            {
                options.KnownProxies.Add(address);
            }
        });
        return true;
    }
}

/// <summary>Browser caching of static files.</summary>
public static class StaticFileCaching
{
    public static void Apply(StaticFileResponseContext context)
    {
        var request = context.Context.Request;
        var headers = context.Context.Response.Headers;

        if (request.Query.ContainsKey("v"))
        {
            // asp-append-version="true" URLs change whenever the file changes.
            headers.CacheControl = "public,max-age=31536000,immutable";
        }
        else if (request.Path.StartsWithSegments("/uploads"))
        {
            headers.CacheControl = "public,max-age=2592000"; // file names are unique (GUID) and never reused
        }
        else if (request.Path.StartsWithSegments("/lib") || request.Path.StartsWithSegments("/images"))
        {
            headers.CacheControl = "public,max-age=604800";
        }
        else
        {
            headers.CacheControl = "public,max-age=3600";
        }
    }
}
