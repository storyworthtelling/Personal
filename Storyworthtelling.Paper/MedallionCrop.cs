using SkiaSharp;

namespace Storyworthtelling.Paper;

/// <summary>
/// Cutting a square out of a photo and re-encoding it small, for the maker's mark (DESIGN.md §13).
///
/// <para>Here rather than in the app because this is the library that owns Skia. The head that picks
/// the photo hands over bytes and gets bytes back, and never names a Skia type — which keeps the one
/// project that cannot be compiled outside CI free of a dependency it would otherwise be guessing
/// about.</para>
///
/// <para><b>Every failure is null, never an exception.</b> The bytes come from a file picker, so
/// "this is not an image" is an ordinary Tuesday, and it must cost a signature rather than a crash
/// in Settings. <c>SKBitmap.Decode</c> does not return null on unreadable input — it throws from
/// inside itself — which is exactly the sort of thing that reaches a screen unhandled.</para>
/// </summary>
public static class MedallionCrop
{
    /// <summary>The photo's dimensions, or null if it is not an image this can read.</summary>
    public static (int Width, int Height)? Measure(byte[] source)
    {
        if (source is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var bitmap = SKBitmap.Decode(source);

            return bitmap is null ? null : (bitmap.Width, bitmap.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The square, scaled to <paramref name="targetEdge"/> and encoded as a PNG. The rectangle is
    /// decided elsewhere and passed in as plain numbers, so the arithmetic stays pure and tested and
    /// this stays the part that only Skia can do.
    /// </summary>
    public static byte[]? SquarePng(
        byte[] source, int sourceX, int sourceY, int sourceEdge, int targetEdge)
    {
        if (sourceEdge <= 0 || targetEdge <= 0)
        {
            return null;
        }

        try
        {
            using var decoded = SKBitmap.Decode(source);
            if (decoded is null)
            {
                return null;
            }

            // A rectangle that does not fit is a plan computed against a different photo. Refuse
            // rather than clamp: a silently different crop is worse than no mark.
            if (sourceX < 0 || sourceY < 0
                || sourceX + sourceEdge > decoded.Width
                || sourceY + sourceEdge > decoded.Height)
            {
                return null;
            }

            using var square = new SKBitmap(sourceEdge, sourceEdge);
            if (!decoded.ExtractSubset(square, SKRectI.Create(sourceX, sourceY, sourceEdge, sourceEdge)))
            {
                return null;
            }

            using var scaled = new SKBitmap(targetEdge, targetEdge);
            using (var canvas = new SKCanvas(scaled))
            {
                canvas.Clear(SKColors.Transparent);
                using var image = SKImage.FromBitmap(square);
                canvas.DrawImage(
                    image,
                    SKRect.Create(0, 0, targetEdge, targetEdge),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
            }

            using var encoded = SKImage.FromBitmap(scaled).Encode(SKEncodedImageFormat.Png, 100);

            return encoded?.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
