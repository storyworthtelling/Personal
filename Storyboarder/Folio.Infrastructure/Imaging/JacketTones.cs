using Folio.Domain.Report;
using SkiaSharp;
using Storyworthtelling.Paper;

namespace Folio.Infrastructure.Imaging;

/// <summary>
/// Reads a jacket's pixels for <see cref="MarkColourway"/> — the one Skia step between an encoded
/// image and a pure decision.
///
/// <para>The jacket is sampled down to a small square first. A histogram over a couple of million
/// pixels answers the same question as one over four thousand, and the quantiser's buckets are
/// coarse enough that the sample cannot change which tone is dominant — it only stops the report
/// spending a second per cover counting pixels it does not need.</para>
/// </summary>
internal static class JacketTones
{
    private const int SampleSize = 64;

    public static MarkColours Read(byte[]? jacket)
    {
        var pixels = Sample(jacket);
        if (pixels.Count == 0)
        {
            // No jacket at all: the mark still has to print, so it takes the page's own ink on its own
            // paper. This is the one case F37's "never a constant" cannot cover, because there is no
            // jacket to ask — and it is reached only when a cover failed to decode.
            return MarkColourway.From([Rgb(ReportPalette.Ink), Rgb(ReportPalette.Ground)]);
        }
        return MarkColourway.From(pixels);
    }

    private static List<int> Sample(byte[]? jacket)
    {
        var pixels = new List<int>();
        if (jacket is not { Length: > 0 })
        {
            return pixels;
        }

        using var bitmap = SKBitmap.Decode(jacket);
        if (bitmap is null)
        {
            return pixels;
        }

        using var small = bitmap.Resize(new SKImageInfo(SampleSize, SampleSize), SKSamplingOptions.Default);
        var source = small ?? bitmap;
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                if (pixel.Alpha > 0)
                {
                    pixels.Add(pixel.Red << 16 | pixel.Green << 8 | pixel.Blue);
                }
            }
        }
        return pixels;
    }

    private static int Rgb(SKColor colour) => colour.Red << 16 | colour.Green << 8 | colour.Blue;
}
