using FurnitureStore.Application.Qr;
using Net.Codecrete.QrCodeGenerator;

namespace FurnitureStore.Infrastructure.Qr;

/// <summary>QR codes via Net.Codecrete.QrCodeGenerator (MIT, no dependencies): SVG for pages, PNG for downloads / print / e-mail.</summary>
public sealed class QrCodeRenderer : IQrCodeRenderer
{
    /// <summary>Light margin around the code, in modules: the QR standard asks for 4 so scanners find the code on any background.</summary>
    public const int QuietZone = 4;

    public string ToSvg(string text) => Encode(text).ToSvgString(QuietZone);

    public byte[] ToPng(string text, int scale) =>
        Encode(text).ToPngBitmap(QuietZone, Math.Clamp(scale, QrLinks.MinPngScale, QrLinks.MaxPngScale));

    // Medium error correction (15%): printed labels still scan when slightly scratched or folded.
    private static QrCode Encode(string text) => QrCode.EncodeText(text, QrCode.Ecc.Medium);
}
