using SkiaSharp;

namespace Storyworthtelling.Paper;

// Draws an image into a destination rectangle, scaled to fit (letterboxed on shaded paper), centered, with
// high-quality resampling. Used by the report's per-scene evidence images so they fill their slot cleanly
// instead of stretching or cropping.
public static class SkiaImageFit
{
    private static readonly SKColor PaperShade = SKColor.Parse(PaperColorTokens.PaperShade);
    private static readonly SKSamplingOptions HighQuality = new(SKCubicResampler.Mitchell);

    // Decodes and draws the PNG bytes fit inside dest. Returns false when the bytes can't be decoded (a
    // missing or corrupt image), so the caller can draw a placeholder instead.
    public static bool Draw(SKCanvas canvas, byte[] image, SKRect dest)
    {
        using var bitmap = SKBitmap.Decode(image);
        if (bitmap is null)
            return false;

        using (var shade = new SKPaint { Color = PaperShade, IsAntialias = false })
            canvas.DrawRect(dest, shade);

        var scale = Math.Min(dest.Width / bitmap.Width, dest.Height / bitmap.Height);
        var drawWidth = bitmap.Width * scale;
        var drawHeight = bitmap.Height * scale;
        var target = new SKRect(
            dest.MidX - drawWidth / 2, dest.MidY - drawHeight / 2,
            dest.MidX + drawWidth / 2, dest.MidY + drawHeight / 2);

        using var paint = new SKPaint { IsAntialias = true };
        using var skImage = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(skImage, target, HighQuality, paint);
        return true;
    }
}
