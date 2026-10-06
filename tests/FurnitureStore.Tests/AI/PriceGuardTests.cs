using FurnitureStore.Application.AI;

namespace FurnitureStore.Tests.AI;

public sealed class PriceGuardTests
{
    private static readonly decimal[] Catalog = [12_490_000, 16_900_000, 3_500_000];

    [Theory]
    [InlineData("Giá 16.900.000đ nhé")]
    [InlineData("Chỉ 16,9 triệu")]
    [InlineData("khoảng 12,5 triệu")]       // 12.490.000 rounded: within 1%
    [InlineData("giá 3.500.000 VNĐ")]
    [InlineData("Giá 3tr5 thôi")]
    public void CatalogPrices_AreKept(string text)
    {
        Assert.Equal(text, PriceGuard.Sanitize(text, Catalog, out var replaced));
        Assert.Equal(0, replaced);
    }

    [Theory]
    [InlineData("Giá chỉ 9.990.000đ", "9.990.000đ")]
    [InlineData("rẻ hơn, 7 triệu thôi", "7 triệu")]
    [InlineData("500k là có", "500k")]
    [InlineData("khoảng 15.000.000", "15.000.000")]
    public void InventedPrices_AreReplaced(string text, string invented)
    {
        var result = PriceGuard.Sanitize(text, Catalog, out var replaced);

        Assert.Equal(1, replaced);
        Assert.DoesNotContain(invented, result);
        Assert.Contains(PriceGuard.Replacement, result);
    }

    [Theory]
    [InlineData("Bàn dài 1.800 mm, rộng 900mm, cho 6 người")]
    [InlineData("Bảo hành 24 tháng, giao trong 3 - 5 ngày")]
    [InlineData("Hotline 0909 123 456, năm 2026")]
    [InlineData("Kích thước 1800 x 900 x 750")]
    public void NumbersThatAreNotMoney_AreUntouched(string text)
    {
        Assert.Equal(text, PriceGuard.Sanitize(text, [], out var replaced));
        Assert.Equal(0, replaced);
    }

    [Fact]
    public void AmountsWrittenByTheCustomer_CanBeRepeated()
    {
        var allowed = PriceGuard.AmountsIn("ngân sách khoảng 10 triệu").ToList();

        Assert.Equal([10_000_000m], allowed);
        Assert.Equal("Trong tầm 10 triệu có...", PriceGuard.Sanitize("Trong tầm 10 triệu có...", allowed, out _));
    }
}
