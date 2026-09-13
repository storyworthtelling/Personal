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

    /// <summary>
    /// Draws the image so it <b>fills</b> dest, cropped equally on the overflowing axis and clipped to
    /// the rectangle — <c>object-fit: cover</c>, the fit a framed artwork slot wants.
    ///
    /// <para><see cref="Draw"/> letterboxes on shade, which is right for an evidence image whose whole
    /// frame is the evidence. Inside a keyline, bands of shade read as a rendering failure rather than
    /// as a margin, so a jacket is cropped instead. The crop itself is decided by the caller — pure
    /// and tested — and handed here as <paramref name="source"/> in the image's own pixels.</para>
    /// </summary>
    public static bool DrawCover(SKCanvas canvas, byte[] image, SKRect dest, Func<int, int, SKRect> source)
    {
        using var bitmap = SKBitmap.Decode(image);
        if (bitmap is null)
            return false;

        using var skImage = SKImage.FromBitmap(bitmap);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.Save();
        canvas.ClipRect(dest);
        canvas.DrawImage(skImage, source(bitmap.Width, bitmap.Height), dest, HighQuality, paint);
        canvas.Restore();
        return true;
    }
}
