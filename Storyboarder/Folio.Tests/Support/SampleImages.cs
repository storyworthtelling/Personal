using SkiaSharp;

namespace Folio.Tests.Support;

public static class SampleImages
{
    // A small solid-color PNG, produced via SkiaSharp so tests exercise the real codec.
    public static byte[] Png(int width = 80, int height = 80, SKColor? color = null)
    {
        var info = new SKImageInfo(width, height);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(color ?? SKColors.Red);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // A smooth picture with a little grain, the way a painted illustration is: lossless it is large,
    // as a JPEG it is small. A solid colour cannot show the difference.
    public static byte[] Textured(int width = 1024, int height = 1792)
    {
        var random = new Random(7);
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var grain = random.Next(-5, 6);
                bitmap.SetPixel(x, y, new SKColor(
                    (byte)Math.Clamp(x * 255 / width + grain, 0, 255),
                    (byte)Math.Clamp(y * 255 / height + grain, 0, 255),
                    (byte)Math.Clamp(128 + (x + y) % 40 + grain, 0, 255)));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
