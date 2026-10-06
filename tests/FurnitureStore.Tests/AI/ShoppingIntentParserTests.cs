using FurnitureStore.Application.AI;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.AI;

/// <summary>Vocabulary built from the real seeded catalog (rooms, categories, colors, materials, styles).</summary>
public sealed class CatalogVocabularyFixture : IAsyncLifetime
{
    public ServiceTestHost Host { get; private set; } = null!;
    public CatalogVocabulary Vocabulary { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Host = await ServiceTestHost.CreateAsync();
        Vocabulary = new CatalogVocabulary(await Host.RunAsync(sp => sp.GetRequiredService<ICatalogService>().GetFilterOptionsAsync()));
    }

    public async Task DisposeAsync() => await Host.DisposeAsync();
}

public sealed class ShoppingIntentParserTests(CatalogVocabularyFixture fixture) : IClassFixture<CatalogVocabularyFixture>
{
    private ShoppingIntent Parse(string text) => ShoppingIntentParser.Parse(text, fixture.Vocabulary);

    [Theory]
    [InlineData("Tôi cần bàn ăn khoảng 10 triệu", null, 10_000_000, true)]
    [InlineData("ngân sách 30tr", null, 30_000_000, true)]
    [InlineData("tầm 1,5 triệu thôi", null, 1_500_000, true)]
    [InlineData("khoảng 12tr5", null, 12_500_000, true)]
    [InlineData("giá 10 triệu 500", null, 10_500_000, true)]
    [InlineData("dưới 15 triệu", null, 15_000_000, false)]
    [InlineData("không quá 800k", null, 800_000, false)]
    [InlineData("trên 20 triệu", 20_000_000, null, false)]
    [InlineData("từ 5 đến 8 triệu", 5_000_000, 8_000_000, false)]
    [InlineData("5 - 8tr", 5_000_000, 8_000_000, false)]
    [InlineData("tu 5 den 8 trieu", 5_000_000, 8_000_000, false)]
    [InlineData("khoảng 12.500.000đ", null, 12_500_000, true)]
    [InlineData("budget 15000000 vnd", null, 15_000_000, true)]
    [InlineData("30 triệu cho phòng khách", null, 30_000_000, true)]
    public void Budget_IsParsed(string text, int? min, int? max, bool approximate)
    {
        var intent = Parse(text);

        Assert.Equal(min, intent.MinBudget is null ? null : (int)intent.MinBudget);
        Assert.Equal(max, intent.MaxBudget is null ? null : (int)intent.MaxBudget);
        if (max.HasValue) Assert.Equal(approximate, intent.BudgetIsApproximate);
    }

    [Theory]
    [InlineData("bàn gỗ màu nâu, dài khoảng 1m8", 1800, null, null)]
    [InlineData("bàn gỗ óc chó dài 2m2 rộng 1m cao 75cm", 2200, 1000, 750)]
    [InlineData("kích thước 1800 x 900 x 750", 1800, 900, 750)]
    [InlineData("bàn 180x90x75", 1800, 900, 750)]
    [InlineData("kệ tivi dài 160cm", 1600, null, null)]
    [InlineData("giường 1m6", 1600, null, null)]
    [InlineData("sofa dài 2.4m", 2400, null, null)]
    public void Dimensions_AreParsed_InMillimetres(string text, int? length, int? width, int? height)
    {
        var intent = Parse(text);

        Assert.Equal(length, intent.LengthMm);
        Assert.Equal(width, intent.WidthMm);
        Assert.Equal(height, intent.HeightMm);
        Assert.Null(intent.MaxBudget); // sizes are never mistaken for prices
    }

    [Fact]
    public void RoomAreaAndPeople_AreParsed()
    {
        var intent = Parse("Tôi có phòng khách 20m2, thích phong cách hiện đại, ngân sách 30 triệu.");

        Assert.Equal(20m, intent.RoomAreaM2);
        Assert.Equal("phong-khach", intent.RoomSlug);
        Assert.Contains(intent.Styles, s => s.Slug == "hien-dai");
        Assert.Equal(30_000_000m, intent.MaxBudget);
        Assert.Null(intent.LengthMm);

        var family = Parse("Tôi cần một bộ bàn ăn cho gia đình 6 người, khoảng 10 triệu.");
        Assert.Equal(6, family.People);
        Assert.Equal("bo-ban-an", Assert.Single(family.Categories).Slug); // not also "bàn ăn"
        Assert.Equal("phong-an", family.RoomSlug);
        Assert.Equal(10_000_000m, family.MaxBudget);
    }

    [Theory]
    [InlineData("tôi thích sofa màu xám", "sofa", "xam")]
    [InlineData("so pha mau xam", "sofa", "xam")]
    [InlineData("tủ quần áo màu trắng", "tu-quan-ao", "trang")]
    [InlineData("tu quan ao mau trang", "tu-quan-ao", "trang")]
    [InlineData("ghế văn phòng màu đen", "ghe-van-phong", "den")]
    [InlineData("ke tivi go oc cho", "ke-tivi", null)]
    public void CategoriesAndColors_WithOrWithoutAccents(string text, string category, string? color)
    {
        var intent = Parse(text);

        Assert.Contains(intent.Categories, c => c.Slug == category);
        if (color is not null) Assert.Contains(intent.Colors, c => c.Slug == color);
    }

    [Fact]
    public void AmbiguousWordsWithoutAccents_AreNotMisread()
    {
        // "den" here means "đến" (to), not black; "gỗ công nghiệp" is a material, not the industrial style.
        var intent = Parse("tu 5 den 8 trieu, ban lam viec go cong nghiep");

        Assert.DoesNotContain(intent.Colors, c => c.Slug == "den");
        Assert.DoesNotContain(intent.Styles, s => s.Slug == "cong-nghiep");
        Assert.Contains(intent.Materials, m => m.Slug == "mdf-chong-am");
        Assert.Contains(intent.Categories, c => c.Slug == "ban-lam-viec");
    }

    [Fact]
    public void MaterialsAndStyles_UseSynonyms()
    {
        var intent = Parse("Sofa da thật phong cách Bắc Âu hoặc japandi, chân sắt");

        Assert.Contains(intent.Materials, m => m.Slug == "da-that");
        Assert.Contains(intent.Materials, m => m.Slug == "thep-son-tinh-dien");
        Assert.Contains(intent.Styles, s => s.Slug == "bac-au");
        Assert.Contains(intent.Styles, s => s.Slug == "japandi");
    }

    [Fact]
    public void SmallTalk_HasNoProductNeeds()
    {
        Assert.False(Parse("Xin chào shop").HasProductNeeds);
        Assert.False(Parse("Chính sách bảo hành thế nào?").HasProductNeeds);
        Assert.True(Parse("bàn trà").HasProductNeeds);
    }

    [Fact]
    public void Describe_SummarisesTheIntent()
    {
        var text = Parse("bộ bàn ăn 6 người màu nâu óc chó khoảng 10 triệu").Describe();

        Assert.Contains("bộ bàn ăn", text);
        Assert.Contains("6 người", text);
        Assert.Contains("khoảng 10 triệu", text);
    }
}
