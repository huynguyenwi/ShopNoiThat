using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FurnitureStore.Tests.Infrastructure;
using static FurnitureStore.Tests.Infrastructure.WebTestHelpers;

namespace FurnitureStore.Tests.Web;

public sealed partial class CatalogWebTests(FurnitureStoreWebApplicationFactory factory) : IClassFixture<FurnitureStoreWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task HomePage_ShowsProductSectionsFromDatabase()
    {
        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("Sản phẩm nổi bật", html);
        Assert.Contains("Sản phẩm mới", html);
        Assert.Contains("Đang giảm giá", html);
        Assert.Contains("/products/ghe-an-go-soi-lung-cong", html); // best-selling featured product
        Assert.Contains("mega-menu", html);
    }

    [Fact]
    public async Task ProductList_RendersFiltersAndCards()
    {
        var response = await factory.CreateClient().GetAsync("/products?category=phong-an&sort=price-asc");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1 class=\"page-title\">Phòng ăn</h1>", html);
        Assert.Contains("data-filter-form", html);
        Assert.Contains("product-card", html);
        Assert.Contains("rel=\"canonical\" href=\"https://localhost:7160/products?category=phong-an\"", html);
    }

    [Fact]
    public async Task ProductList_AjaxRequest_ReturnsOnlyResultsPartial()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/products?q=sofa");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var response = await factory.CreateClient().SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("<html", html);
        Assert.Contains("results-toolbar", html);
        Assert.Contains("Sofa", html);
    }

    [Fact]
    public async Task ProductList_WithMaliciousQuery_IsEncoded()
    {
        var html = await factory.CreateClient().GetStringAsync("/products?q=%3Cimg%20src%3Dx%20onerror%3Dalert(1)%3E");

        Assert.DoesNotContain("<img src=x onerror=alert(1)>", html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
    }

    [Fact]
    public async Task ProductDetail_BySlug_HasVariantsSeoAndStructuredData()
    {
        var response = await factory.CreateClient().GetAsync("/products/ban-an-go-oc-cho-mat-lien-walnut");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1 class=\"product-title\">Bàn ăn gỗ óc chó mặt liền Walnut</h1>", html);
        Assert.Contains("application/ld+json", html);
        Assert.Contains("\"@type\":\"Product\"", html);
        Assert.Contains("property=\"og:type\" content=\"product\"", html);
        Assert.Contains("id=\"productData\"", html);
        Assert.Contains("data-option=\"size\"", html);
        Assert.Contains("BA-WALNUT-NOC-", html);
    }

    [Fact]
    public async Task ProductDetail_UnknownSlug_Returns404Page()
    {
        var response = await factory.CreateClient().GetAsync("/products/khong-co-san-pham-nay");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Không tìm thấy trang", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Api_Products_ReturnsPagedEnvelope()
    {
        var body = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/products?category=sofa&pageSize=2");

        Assert.True(body.GetProperty("success").GetBoolean());
        var data = body.GetProperty("data");
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
        Assert.Equal(4, data.GetProperty("totalCount").GetInt32());
        Assert.StartsWith("/products/", data.GetProperty("items")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Api_ProductById_AndSearch_AndCategories()
    {
        var client = factory.CreateClient();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/products?pageSize=1");
        var id = list.GetProperty("data").GetProperty("items")[0].GetProperty("id").GetInt32();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/products/{id}");
        var search = await client.GetFromJsonAsync<JsonElement>("/api/products/search?q=b%C3%A0n%20g%E1%BB%97");
        var categories = await client.GetFromJsonAsync<JsonElement>("/api/categories");
        var missing = await client.GetAsync("/api/products/999999");

        Assert.True(detail.GetProperty("data").GetProperty("variants").GetArrayLength() > 0);
        Assert.True(search.GetProperty("data").GetArrayLength() > 0);
        Assert.Equal(5, categories.GetProperty("data").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Api_ExceptionsInsideApi_AreReturnedAsJsonEnvelope()
    {
        // NotFoundException thrown by the service must not be rendered as the HTML error page.
        var response = await factory.CreateClient().GetAsync("/api/products/999999");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Contains("sản phẩm", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Placeholder_ReturnsCacheableSvgWithStrictCsp()
    {
        var response = await factory.CreateClient().GetAsync("/images/placeholder/sofa.svg?color=6b7f59&bg=f1e9dd");
        var svg = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("#6b7f59", svg);
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("max-age=31536000", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/images/placeholder/unknown.svg")]
    [InlineData("/images/placeholder/..%2Fsecret.svg")]
    public async Task Placeholder_UnknownShape_Returns404(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Placeholder_IgnoresInjectedColorValues()
    {
        var svg = await factory.CreateClient().GetStringAsync("/images/placeholder/chair.svg?color=%22%3E%3Cscript%3Ealert(1)%3C%2Fscript%3E");

        Assert.DoesNotContain("<script", svg);
        Assert.Contains("#b07d4f", svg);
    }

    // ------------------------------------------------------------------ Admin API authorization

    [Fact]
    public async Task AdminApi_Anonymous_Gets401Json()
    {
        var response = await factory.CreateClient().GetAsync("/api/admin/products");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(body.GetProperty("success").GetBoolean());
    }

    [Theory]
    [InlineData("GET", "/api/admin/products")]
    [InlineData("GET", "/api/admin/products/1")]
    [InlineData("DELETE", "/api/admin/products/1")]
    public async Task AdminApi_CustomerRole_Gets403(string method, string url)
    {
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword);
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync(client));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task AdminApi_Admin_FullCrudLifecycle()
    {
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        var csrf = await GetCsrfTokenAsync(client);
        var categories = await client.GetFromJsonAsync<JsonElement>("/api/categories");
        var categoryId = categories.GetProperty("data")[0].GetProperty("children")[0].GetProperty("id").GetInt32();

        // Create
        var create = new HttpRequestMessage(HttpMethod.Post, "/api/admin/products")
        {
            Content = JsonContent.Create(new
            {
                name = "Kệ API thử nghiệm",
                sku = "API-KE-01",
                categoryId,
                furnitureType = "Shelf",
                status = "Active",
                basePrice = 1_500_000,
                stockQuantity = 5
            })
        };
        create.Headers.Add("X-CSRF-TOKEN", csrf);
        var created = await client.SendAsync(create);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = createdBody.GetProperty("data").GetProperty("productId").GetInt32();

        // Read (admin payload carries the concurrency version)
        var edit = await client.GetFromJsonAsync<JsonElement>($"/api/admin/products/{id}");
        var command = edit.GetProperty("data").GetProperty("command");
        Assert.Equal("API-KE-01", command.GetProperty("sku").GetString());

        // Update
        var updatedCommand = JsonSerializer.Deserialize<Dictionary<string, object?>>(command.GetRawText(), Json)!;
        updatedCommand["name"] = "Kệ API đã cập nhật";
        var update = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/products/{id}") { Content = JsonContent.Create(updatedCommand) };
        update.Headers.Add("X-CSRF-TOKEN", csrf);
        var updated = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var publicDetail = await factory.CreateClient().GetFromJsonAsync<JsonElement>($"/api/products/{id}");
        Assert.Equal("Kệ API đã cập nhật", publicDetail.GetProperty("data").GetProperty("name").GetString());

        // Delete
        var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/products/{id}");
        delete.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(delete)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/products/{id}")).StatusCode);
    }

    [Fact]
    public async Task AdminApi_InvalidPayload_Returns400WithErrors()
    {
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/products") { Content = JsonContent.Create(new { name = "", sku = "", categoryId = 0, basePrice = -1 }) };
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync(client));

        var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(raw.StartsWith('{'), $"Expected JSON but got {(int)response.StatusCode} {response.Content.Headers.ContentType}: {raw[..Math.Min(raw.Length, 400)]}");
        var body = JsonSerializer.Deserialize<JsonElement>(raw);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.True(body.GetProperty("errors").GetArrayLength() >= 3);
    }

    [Fact]
    public async Task AdminApi_WithoutCsrfToken_IsRejected()
    {
        var client = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

        var response = await client.DeleteAsync("/api/admin/products/1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------------ Admin MVC pages

    [Theory]
    [InlineData("/admin/products")]
    [InlineData("/admin/products/create")]
    [InlineData("/admin/products/edit/1")]
    [InlineData("/admin/products/details/1")]
    [InlineData("/admin/categories")]
    [InlineData("/admin/categories/create")]
    [InlineData("/admin/attributes/colors")]
    [InlineData("/admin/attributes/materials")]
    [InlineData("/admin/attributes/sizes")]
    [InlineData("/admin/attributes/styles/create")]
    public async Task AdminPages_LoadForAdmin_AndAreForbiddenForCustomers(string url)
    {
        var admin = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        var customer = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.UserEmail, FurnitureStoreWebApplicationFactory.UserPassword);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
        var denied = await customer.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("/account/access-denied", denied.Headers.Location?.ToString());
    }

    [Fact]
    public async Task AdminForm_CreateProductWithVariantRows_ThenEditPageShowsIt()
    {
        var admin = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);
        var createPage = await admin.GetStringAsync("/admin/products/create");
        var categoryId = OptionValueRegex().Match(createPage.Split("name=\"Command.CategoryId\"")[1]).Groups[1].Value;

        var response = await PostFormAsync(admin, "/admin/products/create", "/admin/products/create", new Dictionary<string, string>
        {
            ["Command.Name"] = "Ghế đẩu form admin",
            ["Command.Sku"] = "FORM-GD-01",
            ["Command.CategoryId"] = categoryId,
            ["Command.FurnitureType"] = "Chair",
            ["Command.Status"] = "Active",
            ["Command.WarrantyMonths"] = "12",
            ["Command.Variants.Index"] = "a7",
            ["Command.Variants[a7].Sku"] = "FORM-GD-01-A",
            ["Command.Variants[a7].Price"] = "990000",
            ["Command.Variants[a7].StockQuantity"] = "8",
            ["Command.Variants[a7].LowStockThreshold"] = "2",
            ["Command.Variants[a7].IsActive"] = "true",
            ["defaultVariantKey"] = "a7"
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var editUrl = response.Headers.Location!.ToString();
        Assert.Matches("/admin/products/edit/\\d+", editUrl);
        var editPage = await admin.GetStringAsync(editUrl);
        Assert.Contains("FORM-GD-01-A", editPage);
        Assert.Contains("Đã tạo sản phẩm", editPage);
    }

    [Fact]
    public async Task AdminForm_InvalidInput_ReRendersWithErrors()
    {
        var admin = await CreateSignedInClientAsync(factory, FurnitureStoreWebApplicationFactory.AdminEmail, FurnitureStoreWebApplicationFactory.AdminPassword);

        var response = await PostFormAsync(admin, "/admin/products/create", "/admin/products/create", new Dictionary<string, string>
        {
            ["Command.Name"] = "",
            ["Command.Sku"] = "OK-1",
            ["Command.CategoryId"] = "0",
            ["Command.Variants.Index"] = "0",
            ["Command.Variants[0].Sku"] = "OK-1-A",
            ["Command.Variants[0].Price"] = "0",
            ["Command.Variants[0].StockQuantity"] = "1",
            ["defaultVariantKey"] = "0"
        });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Vui lòng nhập tên sản phẩm.", html);
        Assert.Contains("Vui lòng chọn danh mục.", html);
        Assert.Contains("Biến thể 1: Giá phải lớn hơn 0.", html);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/");
        return WebUtility.HtmlDecode(CsrfMetaRegex().Match(html).Groups[1].Value);
    }

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMetaRegex();

    [GeneratedRegex("<option value=\"(\\d+)\"")]
    private static partial Regex OptionValueRegex();
}
