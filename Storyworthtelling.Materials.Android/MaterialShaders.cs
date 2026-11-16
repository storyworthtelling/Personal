using Android.Graphics;

namespace Storyworthtelling.Materials.Android;

/// <summary>
/// The engine's tiles as Android bitmaps, built once a process and reused by every view (DESIGN.md §2: textures are
/// baked once and never regenerated per frame; 60fps scroll is a merge gate). Touched from the UI thread only.
/// </summary>
public static class MaterialShaders
{
    private static Bitmap? paperGrain;
    private static Bitmap? pencilTooth;

    /// <summary>The paper grain: ink flecks at the grain alpha over transparent, one tile texel a device pixel.</summary>
    public static Bitmap PaperGrain => paperGrain ??= Build(PaperGrainTile.Build(), ink: true);

    /// <summary>The pencil's tooth as an alpha-only bitmap: a shader over it takes its colour from the paint.</summary>
    public static Bitmap PencilTooth => pencilTooth ??= Build(Materials.PencilTooth.Build(), ink: false);

    private static Bitmap Build(MaterialTile tile, bool ink)
    {
        if (!ink)
        {
            var alpha = Bitmap.CreateBitmap(tile.Size, tile.Size, Bitmap.Config.Alpha8!)!;
            alpha.CopyPixelsFromBuffer(Java.Nio.ByteBuffer.Wrap(tile.Values));
            return alpha;
        }

        var pixels = new int[tile.Values.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (tile.Values[i] << 24) | InkRgb;
        }
        return Bitmap.CreateBitmap(pixels, tile.Size, tile.Size, Bitmap.Config.Argb8888!)!;
    }

    // The grain's ink, PaperColorTokens.Ink (#33322B) without its alpha. The flecks are ink on any ground.
    private const int InkRgb = 0x33322B;
}
