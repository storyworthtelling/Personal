using SkiaSharp;

namespace Storyworthtelling.Paper;

/// <summary>
/// The paper grain for generated pages (DESIGN.md §6): the same seeded
/// xorshift noise IconStudio bakes into the app window backgrounds, drawn
/// here as a cached repeating shader over the paper clear. Deterministic —
/// identical inputs always produce identical page bytes.
/// </summary>
public static class PaperGrain
{
    private static readonly Lazy<(SKBitmap Tile, SKShader Shader)> cached = new(Build);
    private static readonly Dictionary<float, SKShader> scaled = [];

    public static void Draw(SKCanvas canvas, int width, int height)
    {
        using var paint = new SKPaint { Shader = cached.Value.Shader };
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
    }

    /// <summary>
    /// The same grain on a page measured in <b>points</b> rather than pixels.
    ///
    /// <para>The tile is 256 units square, which was 1.7 inches on a 150-DPI raster page and would be
    /// 3.5 inches on a point page — the same noise at half the density, which reads as a coarser
    /// paper rather than as the same one. <paramref name="scale"/> puts it back: 72/150 for a page
    /// that used to be rendered at 150 DPI.</para>
    ///
    /// <para>It stays a shader over a small tile rather than a full-page bitmap, which is what lets
    /// §6's always-on texture survive the move to vector at all: Skia's PDF backend writes it as one
    /// tiling pattern carrying one 256-square image, instead of the tens of thousands of drawn dots a
    /// per-mark grain would emit.</para>
    ///
    /// <para><b>The shader is cached, and that is load-bearing rather than tidy.</b> Skia's PDF
    /// backend shares an embedded image between pages by <i>instance</i>, so a shader built fresh on
    /// each call embeds the whole tile again on every sheet: measured at 17.5 KB per page against 640
    /// bytes when one instance is reused, which is 2.6 MB of duplicated noise on a 150-page report and
    /// most of what the move to vector was meant to remove. Reused, the tile is paid for once.</para>
    /// </summary>
    public static void Draw(SKCanvas canvas, float width, float height, float scale)
    {
        using var paint = new SKPaint { Shader = Scaled(scale) };
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
    }

    private static SKShader Scaled(float scale)
    {
        lock (scaled)
        {
            if (!scaled.TryGetValue(scale, out var shader))
            {
                shader = SKShader.CreateBitmap(
                    cached.Value.Tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                    SKMatrix.CreateScale(scale, scale));
                scaled[scale] = shader;
            }
            return shader;
        }
    }

    private static (SKBitmap, SKShader) Build()
    {
        var size = PaperColorTokens.Grain.TileSize;
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var ink = SKColor.Parse(PaperColorTokens.Ink);
        var state = PaperColorTokens.Grain.Seed;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                state = XorShift(state);
                bitmap.SetPixel(x, y, state % PaperColorTokens.Grain.Density == 0
                    ? new SKColor(ink.Red, ink.Green, ink.Blue, (byte)PaperColorTokens.Grain.Alpha)
                    : SKColors.Transparent);
            }
        }
        return (bitmap, SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat));
    }

    private static uint XorShift(uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}
