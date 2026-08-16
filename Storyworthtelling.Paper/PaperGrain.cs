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

    public static void Draw(SKCanvas canvas, int width, int height)
    {
        using var paint = new SKPaint { Shader = cached.Value.Shader };
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
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
