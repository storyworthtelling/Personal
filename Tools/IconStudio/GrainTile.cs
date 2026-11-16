using SkiaSharp;
using Storyworthtelling.Materials;

namespace IconStudio;

/// <summary>
/// The paper-grain tile (DESIGN.md §2) as the PNG every native screen's window background repeats. The tile itself is
/// the materials engine's (<see cref="PaperGrainTile"/>), the one the MAUI grounds and every document draw too, so
/// regeneration is byte-identical and the screen and the page are one paper.
/// </summary>
public static class GrainTile
{
    public const int TileSize = Grain.TileSize;

    public static byte[] RenderPng()
    {
        var tile = PaperGrainTile.Build();
        using var bitmap = new SKBitmap(new SKImageInfo(tile.Size, tile.Size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var ink = SKColor.Parse(Tokens.InkHex);
        for (var y = 0; y < tile.Size; y++)
        {
            for (var x = 0; x < tile.Size; x++)
            {
                var alpha = tile.At(x, y);
                bitmap.SetPixel(x, y, alpha == 0 ? SKColors.Transparent : new SKColor(ink.Red, ink.Green, ink.Blue, alpha));
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
