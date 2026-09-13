using SkiaSharp;

namespace IconStudio;

/// <summary>
/// The paper-grain tile (DESIGN.md §6): deterministic xorshift-seeded ink
/// noise. One algorithm, one seed — regeneration is byte-identical, and the
/// Skia renderers reproduce the identical pattern from the same constants.
/// </summary>
public static class GrainTile
{
    public const int TileSize = 256;
    public const byte Alpha = 32;  // of 255 — ~12.5% ink
    public const int Density = 6;  // ~1 in Density pixels carries grain
    public const uint Seed = 1913;

    public static byte[] RenderPng()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(TileSize, TileSize, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var ink = SKColor.Parse(Tokens.InkHex);
        var state = Seed;
        for (var y = 0; y < TileSize; y++)
        {
            for (var x = 0; x < TileSize; x++)
            {
                state = XorShift(state);
                bitmap.SetPixel(x, y, state % Density == 0
                    ? new SKColor(ink.Red, ink.Green, ink.Blue, Alpha)
                    : SKColors.Transparent);
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static uint XorShift(uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}
