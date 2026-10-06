using System.Globalization;
using System.Text;
using System.Xml;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Web.Controllers;

/// <summary>robots.txt and sitemap.xml, generated from the live catalog.</summary>
public sealed class SeoController(ICatalogService catalog, IOptions<ApplicationSettings> siteOptions, IOptions<SeoSettings> seoOptions) : Controller
{
    /// <summary>
    /// Areas that must not be indexed (also marked noindex in the pages themselves). "/q/" and "/qr/" are the QR code
    /// redirects and images; the product pages they lead to are in the sitemap.
    /// </summary>
    public static readonly string[] PrivatePaths = ["/admin", "/account", "/cart", "/checkout", "/wishlist", "/api", "/hubs", "/q/", "/qr/"];

    private string BaseUrl => siteOptions.Value.BaseUrl.TrimEnd('/');

    [HttpGet("/robots.txt")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult Robots()
    {
        var text = new StringBuilder("User-agent: *\n");
        if (!seoOptions.Value.AllowIndexing)
        {
            text.Append("Disallow: /\n"); // staging / test environments
        }
        else
        {
            foreach (var path in PrivatePaths)
            {
                text.Append("Disallow: ").Append(path).Append('\n');
            }

            text.Append("Allow: /\n\n");
            text.Append("Sitemap: ").Append(BaseUrl).Append("/sitemap.xml\n");
        }

        return Content(text.ToString(), "text/plain", Encoding.UTF8);
    }

    [HttpGet("/sitemap.xml")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Sitemap(CancellationToken cancellationToken)
    {
        var categories = await catalog.GetCategoryTreeAsync(cancellationToken);
        var products = await catalog.GetSitemapEntriesAsync(cancellationToken);

        using var buffer = new MemoryStream();
        using (var xml = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            WriteUrl(xml, "/", "daily", "1.0");
            WriteUrl(xml, "/products", "daily", "0.9");
            foreach (var room in categories)
            {
                WriteUrl(xml, "/products?category=" + Uri.EscapeDataString(room.Slug), "weekly", "0.8");
                foreach (var child in room.Children)
                {
                    WriteUrl(xml, "/products?category=" + Uri.EscapeDataString(child.Slug), "weekly", "0.7");
                }
            }

            foreach (var product in products)
            {
                WriteUrl(xml, "/products/" + Uri.EscapeDataString(product.Slug), "weekly", "0.8", product.LastModifiedUtc);
            }

            WriteUrl(xml, "/tu-van", "monthly", "0.6");
            WriteUrl(xml, "/bao-gia", "monthly", "0.6");
            WriteUrl(xml, "/contact", "monthly", "0.5");

            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        return File(buffer.ToArray(), "application/xml; charset=utf-8");
    }

    private void WriteUrl(XmlWriter xml, string path, string changeFrequency, string priority, DateTime? lastModifiedUtc = null)
    {
        xml.WriteStartElement("url");
        xml.WriteElementString("loc", BaseUrl + path);
        if (lastModifiedUtc is DateTime modified)
        {
            xml.WriteElementString("lastmod", DateTime.SpecifyKind(modified, DateTimeKind.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        xml.WriteElementString("changefreq", changeFrequency);
        xml.WriteElementString("priority", priority);
        xml.WriteEndElement();
    }
}
