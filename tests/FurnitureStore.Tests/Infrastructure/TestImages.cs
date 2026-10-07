using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace FurnitureStore.Tests.Infrastructure;

/// <summary>Real, decodable pictures for upload tests.</summary>
public static class TestImages
{
    public static readonly Rgba32 TopLeft = new(220, 30, 30);      // red
    public static readonly Rgba32 TopRight = new(30, 200, 30);     // green
    public static readonly Rgba32 BottomLeft = new(30, 30, 220);   // blue
    public static readonly Rgba32 BottomRight = new(240, 240, 240);

    /// <summary>Four coloured quarters, so rotations and crops can be checked.</summary>
    public static Image<Rgba32> Quarters(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = (x < width / 2, y < height / 2) switch
                    {
                        (true, true) => TopLeft,
                        (false, true) => TopRight,
                        (true, false) => BottomLeft,
                        _ => BottomRight
                    };
                }
            }
        });
        return image;
    }

    /// <param name="orientation">EXIF orientation as written by a phone camera (6 = turn 90° clockwise to view).</param>
    public static byte[] Jpeg(int width, int height, ushort orientation = 1, bool withGps = false)
    {
        using var image = Quarters(width, height);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
        image.Metadata.ExifProfile.SetValue(ExifTag.Make, "TestCam");
        if (withGps)
        {
            image.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
            image.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitude, [new Rational(10, 1), new Rational(46, 1), new Rational(0, 1)]);
        }

        using var output = new MemoryStream();
        image.Save(output, new JpegEncoder { Quality = 95 });
        return output.ToArray();
    }

    public static byte[] Png(int width, int height, bool transparent = false)
    {
        using var image = Quarters(width, height);
        if (transparent)
        {
            for (var y = 0; y < height / 4; y++)
            {
                for (var x = 0; x < width / 4; x++)
                {
                    image[x, y] = new Rgba32(0, 0, 0, 0);
                }
            }
        }

        using var output = new MemoryStream();
        image.Save(output, new PngEncoder());
        return output.ToArray();
    }

    /// <summary>Colour at a relative position (0 - 1) of a stored picture.</summary>
    public static Rgba32 PixelAt(Image<Rgba32> image, double x, double y) =>
        image[(int)(x * (image.Width - 1)), (int)(y * (image.Height - 1))];

    public static bool IsClose(Rgba32 actual, Rgba32 expected, int tolerance = 40) =>
        Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance && Math.Abs(actual.B - expected.B) <= tolerance;
}
