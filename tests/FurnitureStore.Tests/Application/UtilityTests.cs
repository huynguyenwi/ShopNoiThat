using FurnitureStore.Application.Common.Utilities;

namespace FurnitureStore.Tests.Application;

public sealed class UtilityTests
{
    [Theory]
    [InlineData("Bàn ăn gỗ óc chó 180cm", "ban-an-go-oc-cho-180cm")]
    [InlineData("  Đèn   Đứng -- Phòng Khách! ", "den-dung-phong-khach")]
    [InlineData("Sofa góc chữ L Milano (da bò thật)", "sofa-goc-chu-l-milano-da-bo-that")]
    [InlineData("Giường 1m8 x 2m", "giuong-1m8-x-2m")]
    [InlineData("Ghế ĂN – Mây Tre", "ghe-an-may-tre")]
    [InlineData("", "")]
    [InlineData("!!!", "")]
    public void SlugGenerator_HandlesVietnamese(string input, string expected)
    {
        Assert.Equal(expected, SlugGenerator.Generate(input));
    }

    [Fact]
    public void SlugGenerator_RespectsMaxLength_WithoutTrailingDash()
    {
        var slug = SlugGenerator.Generate("bàn ăn gỗ óc chó mặt liền", maxLength: 11);

        Assert.Equal("ban-an-go-o", slug);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public void PlaceholderUrl_IsBuiltFromWhitelistedShapeAndNormalizedHex()
    {
        var url = PlaceholderImages.BuildUrl("sofa", "#6B7F59", "#F1E9DD");

        Assert.Equal("/images/placeholder/sofa.svg?color=6b7f59&bg=f1e9dd", url);
    }

    [Theory]
    [InlineData("../etc/passwd", "#000000")]
    [InlineData("sofa", "red")]
    [InlineData("sofa", "#12345")]
    [InlineData("sofa", "\"><script>")]
    public void PlaceholderUrl_RejectsUnknownShapesAndInvalidColors(string shape, string color)
    {
        Assert.Throws<ArgumentException>(() => PlaceholderImages.BuildUrl(shape, color));
    }
}
