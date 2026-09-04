using SkiaSharp;

namespace IconStudio;

/// <summary>
/// The owner's medallion (DESIGN.md §13). The source image lives at
/// /Branding/owner.png — a placeholder serif monogram until the owner drops
/// in their real photo — and every consumer copy is that source circle-masked
/// with a hairline ring.
/// </summary>
public static class BrandingMedallion
{
    /// <summary>The placeholder source: paper square, Serif monogram, one terracotta dot.</summary>
    public static byte[] PlaceholderSource(SKTypeface serif, int size = 512)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        var canvas = surface.Canvas;
        canvas.Clear(Tokens.Paper);

        using var font = new SKFont(serif, size * 0.52f);
        using var inkPaint = new SKPaint { Color = Tokens.Ink, IsAntialias = true };
        const string monogram = "S";
        var width = font.MeasureText(monogram);
        canvas.DrawText(monogram, (size - width) / 2f, size * 0.63f, font, inkPaint);

        using var accent = new SKPaint { Color = Tokens.Accent, IsAntialias = true };
        canvas.DrawCircle(size / 2f, size * 0.78f, size * 0.030f, accent);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Circle-masks any square-ish source and adds the hairline ring.</summary>
    public static byte[] Medallion(byte[] sourcePng, int size = 512)
    {
        using var source = SKBitmap.Decode(sourcePng)
            ?? throw new InvalidOperationException("Branding source image could not be decoded.");
        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var center = size / 2f;
        var radius = size / 2f - size * 0.02f;
        using (var clip = new SKPath())
        {
            clip.AddCircle(center, center, radius);
            canvas.Save();
            canvas.ClipPath(clip, antialias: true);
            // Center-crop the source to a square, then fill the circle.
            var crop = Math.Min(source.Width, source.Height);
            var src = new SKRect(
                (source.Width - crop) / 2f, (source.Height - crop) / 2f,
                (source.Width + crop) / 2f, (source.Height + crop) / 2f);
            canvas.DrawBitmap(source, src, new SKRect(0, 0, size, size));
            canvas.Restore();
        }

        using var ring = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(2f, size * 0.008f),
            Color = Tokens.Hairline,
            IsAntialias = true,
        };
        canvas.DrawCircle(center, center, radius, ring);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
