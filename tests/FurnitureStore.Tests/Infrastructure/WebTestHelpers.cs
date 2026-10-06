using System.Net;
using System.Text.RegularExpressions;

namespace FurnitureStore.Tests.Infrastructure;

/// <summary>Helpers to drive MVC forms (with anti-forgery tokens) from integration tests.</summary>
public static partial class WebTestHelpers
{
    /// <summary>GETs <paramref name="pageUrl"/> and returns the anti-forgery token of its first form.</summary>
    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string pageUrl)
    {
        var response = await client.GetAsync(pageUrl);
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryInputRegex().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException($"No anti-forgery token found on {pageUrl} (status {(int)response.StatusCode}).");
        }

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>Loads the form page for its token, then POSTs the fields to <paramref name="postUrl"/>.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string formPageUrl, string postUrl, IDictionary<string, string> fields)
    {
        var token = await GetAntiforgeryTokenAsync(client, formPageUrl);
        var content = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token };
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(content));
    }

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, string? returnUrl = null)
    {
        var fields = new Dictionary<string, string> { ["Email"] = email, ["Password"] = password, ["RememberMe"] = "false" };
        if (returnUrl is not null)
        {
            fields["ReturnUrl"] = returnUrl;
        }

        return PostFormAsync(client, "/account/login", "/account/login", fields);
    }

    public static async Task<HttpClient> CreateSignedInClientAsync(FurnitureStoreWebApplicationFactory factory, string email, string password)
    {
        var client = factory.CreateClient();
        var response = await LoginAsync(client, email, password);
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Login for {email} failed with status {(int)response.StatusCode}.");
        }

        return client;
    }

    public static async Task<string> RegisterAsync(HttpClient client, string? email = null, string password = "Khach@Hang123")
    {
        email ??= $"user-{Guid.NewGuid():N}@example.com";
        var response = await PostFormAsync(client, "/account/register", "/account/register", new Dictionary<string, string>
        {
            ["FullName"] = "Khách Thử Nghiệm",
            ["Email"] = email,
            ["PhoneNumber"] = "0912345678",
            ["Password"] = password,
            ["ConfirmPassword"] = password,
            ["AcceptTerms"] = "true"
        });

        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            var html = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Registration failed ({(int)response.StatusCode}): {html[..Math.Min(html.Length, 300)]}");
        }

        return email;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryInputRegex();
}
