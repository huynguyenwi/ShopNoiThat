using System.Buffers.Binary;
using System.Xml.Linq;
using FurnitureStore.Application.Qr;
using FurnitureStore.Infrastructure.Qr;

namespace FurnitureStore.Tests.Infrastructure;

public sealed class QrCodeRendererTests
{
    private readonly QrCodeRenderer _renderer = new();

    [Fact]
    public void Svg_IsAWellFormedSquareDrawing_WithTheQuietZone()
    {
        var svg = XDocument.Parse(_renderer.ToSvg("https://nhamoc.vn/q/p/12"));

        Assert.Equal("svg", svg.Root!.Name.LocalName);
        var viewBox = svg.Root.Attribute("viewBox")!.Value.Split(' ').Select(int.Parse).ToArray();
        Assert.Equal(viewBox[2], viewBox[3]);
        Assert.Equal(0, (viewBox[2] - 2 * QrCodeRenderer.QuietZone - 17) % 4); // QR sizes are 21, 25, 29... modules (17 + 4 x version)
        Assert.DoesNotContain("<script", svg.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(QrLinks.MinPngScale)]
    [InlineData(QrLinks.DefaultPngScale)]
    public void Png_HasTheExpectedPixelSize(int scale)
    {
        var png = _renderer.ToPng("https://nhamoc.vn/q/o/DH260930-4A7NA", scale);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));   // IHDR
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        Assert.Equal(width, height);
        Assert.Equal(0, width % scale);
        Assert.Equal(0, (width / scale - 2 * QrCodeRenderer.QuietZone - 17) % 4);
    }

    [Fact]
    public void DifferentAddresses_GiveDifferentCodes_AndTheSameAddressTheSameCode()
    {
        Assert.Equal(_renderer.ToSvg("https://nhamoc.vn/q/p/1"), _renderer.ToSvg("https://nhamoc.vn/q/p/1"));
        Assert.NotEqual(_renderer.ToSvg("https://nhamoc.vn/q/p/1"), _renderer.ToSvg("https://nhamoc.vn/q/p/2"));
    }

    [Theory]
    [InlineData("DH260930-4A7NA", true)]
    [InlineData("dh260930-4a7na", false)] // callers upper-case first
    [InlineData("DH<script>", false)]
    [InlineData("AB", false)]
    public void OrderCodeFormat(string code, bool valid) => Assert.Equal(valid, QrLinks.IsOrderCode(code));
}
