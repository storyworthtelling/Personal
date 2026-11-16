using Android.Graphics;

namespace Storyworthtelling.Materials.Android;

/// <summary>
/// The engine's tiles as Android bitmaps, built once a process and reused by every view (DESIGN.md §2: textures are
/// baked once and never regenerated per frame; 60fps scroll is a merge gate). Touched from the UI thread only.
/// Alpha-only bitmaps take their colour from the paint that draws them, so one tile serves every ink.
/// </summary>
public static class MaterialShaders
{
    private static Bitmap? paperGrain;
    private static Bitmap? paperGrainPressed;
    private static Bitmap? pencilTooth;
    private static Bitmap? pencilToothPressed;
    private static Bitmap? markerStreaks;
    private static Bitmap? markerStreaksPressed;
    private static Bitmap? highlighterStreaks;

    /// <summary>The paper grain: ink flecks at the grain alpha over transparent, one tile texel a device pixel.</summary>
    public static Bitmap PaperGrain => paperGrain ??= Ink(PaperGrainTile.Build());

    /// <summary>The paper grain while a surface is held: each fleck half again as deep.</summary>
    public static Bitmap PaperGrainPressed => paperGrainPressed ??= Ink(PaperGrainTile.Build().Grained(MaterialStrength.PressLift));

    /// <summary>The pencil's tooth, alpha only.</summary>
    public static Bitmap PencilTooth => pencilTooth ??= Alpha(Materials.PencilTooth.Build());

    /// <summary>The pencil's tooth while held.</summary>
    public static Bitmap PencilToothPressed => pencilToothPressed ??= Alpha(Materials.PencilTooth.Build().Lifted(MaterialStrength.PressLift));

    /// <summary>The marker's streaks: one texel wide, a row each, repeated along the stroke.</summary>
    public static Bitmap MarkerStreaks => markerStreaks ??= Rows(Streaks.Marker());

    /// <summary>The marker's streaks while held.</summary>
    public static Bitmap MarkerStreaksPressed => markerStreaksPressed ??= Rows(Streaks.Marker(MaterialStrength.PressLift));

    /// <summary>The highlighter's streaks, as its own alphas.</summary>
    public static Bitmap HighlighterStreaks => highlighterStreaks ??= Rows(Streaks.Highlighter());

    /// <summary>A repeating shader over <paramref name="bitmap"/>, one texel <paramref name="texel"/> canvas units across.</summary>
    public static BitmapShader Tiled(Bitmap bitmap, float texel)
    {
        var shader = new BitmapShader(bitmap, Shader.TileMode.Repeat!, Shader.TileMode.Repeat!);
        var matrix = new Matrix();
        matrix.SetScale(texel, texel);
        shader.SetLocalMatrix(matrix);
        return shader;
    }

    private static Bitmap Alpha(MaterialTile tile)
    {
        var bitmap = Bitmap.CreateBitmap(tile.Size, tile.Size, Bitmap.Config.Alpha8!)!;
        bitmap.CopyPixelsFromBuffer(Java.Nio.ByteBuffer.Wrap(tile.Values));
        return bitmap;
    }

    private static Bitmap Rows(byte[] rows)
    {
        // Alpha8 rows are padded to four bytes, so the strip is four texels wide (all four alike) rather than one.
        const int width = 4;
        var values = new byte[rows.Length * width];
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < width; x++)
            {
                values[(y * width) + x] = rows[y];
            }
        }
        var bitmap = Bitmap.CreateBitmap(width, rows.Length, Bitmap.Config.Alpha8!)!;
        bitmap.CopyPixelsFromBuffer(Java.Nio.ByteBuffer.Wrap(values));
        return bitmap;
    }

    private static Bitmap Ink(MaterialTile tile)
    {
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
