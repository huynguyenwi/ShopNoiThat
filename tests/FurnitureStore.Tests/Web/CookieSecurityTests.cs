using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FurnitureStore.Tests.Web;

/// <summary>Cookies are HTTPS-only outside Development; in Development plain HTTP works too (VS "http" profile, LAN tests).</summary>
public sealed class CookieSecurityTests
{
    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "FurnitureStore.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static HttpContext Request(string environment, bool https) => new DefaultHttpContext
    {
        RequestServices = new ServiceCollection().AddSingleton<IHostEnvironment>(new Environment(environment)).BuildServiceProvider(),
        Request = { Scheme = https ? "https" : "http" }
    };

    [Theory]
    [InlineData("Development", CookieSecurePolicy.SameAsRequest)]
    [InlineData("Production", CookieSecurePolicy.Always)]
    [InlineData("Staging", CookieSecurePolicy.Always)]
    [InlineData("Testing", CookieSecurePolicy.Always)]
    public void AuthAndAntiforgeryCookies_AreHttpsOnly_OutsideDevelopment(string environment, CookieSecurePolicy expected)
    {
        Assert.Equal(expected, CookieSecurity.PolicyFor(new Environment(environment)));
    }

    [Theory]
    [InlineData("Production", false, true)]   // never sent over plain HTTP in production
    [InlineData("Production", true, true)]
    [InlineData("Development", true, true)]
    [InlineData("Development", false, false)] // http://localhost:5243 or http://<LAN IP>:5243 while developing
    public void CartChatAndAiCookies_FollowTheSameRule(string environment, bool https, bool expectedSecure)
    {
        Assert.Equal(expectedSecure, CookieSecurity.IsSecure(Request(environment, https)));
    }
}
