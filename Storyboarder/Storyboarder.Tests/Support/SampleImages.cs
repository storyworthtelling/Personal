using SkiaSharp;

namespace Storyboarder.Tests.Support;

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
}
