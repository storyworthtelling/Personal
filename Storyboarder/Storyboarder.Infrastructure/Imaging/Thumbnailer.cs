using SkiaSharp;

namespace Storyboarder.Infrastructure.Imaging;

/// <summary>
/// Downscales composed pages into library thumbnails so the projects list
/// never decodes full-size pages on its refresh timer.
/// </summary>
public static class Thumbnailer
{
    public static byte[] Downscale(byte[] png, int targetWidth)
    {
        // SKBitmap.Decode throws inconsistently on garbage input; normalize to
        // one descriptive failure so pipeline errors stay readable.
        using var codec = SKCodec.Create(SKData.CreateCopy(png))
            ?? throw new InvalidOperationException("Thumbnail source image could not be decoded.");
        using var source = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException("Thumbnail source image could not be decoded.");
        var scale = targetWidth / (float)source.Width;
        var info = new SKImageInfo(targetWidth, Math.Max(1, (int)(source.Height * scale)));
        using var scaled = source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException("Thumbnail resize failed.");
        using var image = SKImage.FromBitmap(scaled);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
