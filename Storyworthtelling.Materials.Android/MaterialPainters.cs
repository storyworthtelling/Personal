using Android.Graphics;
using Path = Android.Graphics.Path;

namespace Storyworthtelling.Materials.Android;

/// <summary>
/// Draws in the materials of DESIGN.md §2 on an Android canvas. Each painter takes the scale the canvas is in, as
/// canvas units per dp: a native view's canvas is in pixels (the display density), a MAUI GraphicsView's is already in
/// dp (1). That is how one tile stays the same size on paper in both kits.
/// </summary>
public static class MaterialPainters
{
    private static readonly Dictionary<string, Path> Paths = [];
    private static readonly Paint GlyphStroke = new(PaintFlags.AntiAlias)
    {
        StrokeCap = Paint.Cap.Round,
        StrokeJoin = Paint.Join.Round,
    };
    private static readonly Paint Ground = new();
    private static readonly Paint Flecks = new();
    private static BitmapShader? toothShader;
    private static BitmapShader? grainShader;

    /// <summary>
    /// A tab glyph (<see cref="TabGlyphs"/>) in colored pencil (§10): its 2-unit stroke, fitted to
    /// <paramref name="box"/>, in <paramref name="ink"/> with the pencil's tooth.
    /// </summary>
    public static void PencilGlyph(Canvas canvas, string glyph, RectF box, Color ink, float canvasUnitsPerDp)
    {
        var scale = box.Width() / TabGlyphs.Viewport;
        if (scale <= 0)
        {
            return;
        }

        toothShader ??= new BitmapShader(MaterialShaders.PencilTooth, Shader.TileMode.Repeat!, Shader.TileMode.Repeat!);
        // One tooth texel is PencilTexelDp on screen; the path is drawn in glyph units, so the shader is scaled back.
        var texel = MaterialStrength.PencilTexelDp * canvasUnitsPerDp / scale;
        var matrix = new Matrix();
        matrix.SetScale(texel, texel);
        toothShader.SetLocalMatrix(matrix);

        GlyphStroke.SetStyle(Paint.Style.Stroke);
        GlyphStroke.StrokeWidth = TabGlyphs.StrokeWidth;
        GlyphStroke.Color = ink;
        GlyphStroke.SetShader(toothShader);

        var save = canvas.Save();
        canvas.Translate(box.Left, box.Top);
        canvas.Scale(scale, scale);
        canvas.DrawPath(PathFor(glyph), GlyphStroke);
        canvas.RestoreToCount(save);
    }

    /// <summary>
    /// A paper ground (§2): the flat ground colour with the paper grain over it, one grain texel a device pixel, as
    /// every native screen's window background already is.
    /// </summary>
    public static void PaperGround(Canvas canvas, RectF box, Color ground, float canvasUnitsPerPixel)
    {
        Ground.Color = ground;
        canvas.DrawRect(box, Ground);

        grainShader ??= new BitmapShader(MaterialShaders.PaperGrain, Shader.TileMode.Repeat!, Shader.TileMode.Repeat!);
        var matrix = new Matrix();
        matrix.SetScale(canvasUnitsPerPixel, canvasUnitsPerPixel);
        grainShader.SetLocalMatrix(matrix);
        Flecks.SetShader(grainShader);
        canvas.DrawRect(box, Flecks);
    }

    /// <summary>
    /// Readies <paramref name="paint"/> to draw in colored pencil (§2): <paramref name="ink"/> with the tooth, one tooth
    /// texel <see cref="MaterialStrength.PencilTexelDp"/> on screen. For a custom view's own strokes and fills (a
    /// checkbox, the Dial's arc); the caller sets the style and width.
    /// </summary>
    public static Paint Pencil(Paint paint, Color ink, float canvasUnitsPerDp, bool pressed = false)
    {
        paint.AntiAlias = true;
        paint.Color = ink;
        paint.SetShader(MaterialShaders.Tiled(
            pressed ? MaterialShaders.PencilToothPressed : MaterialShaders.PencilTooth,
            MaterialStrength.PencilTexelDp * canvasUnitsPerDp));
        return paint;
    }

    /// <summary>
    /// Readies <paramref name="paint"/> to draw in marker (§2): <paramref name="ink"/> with fibre streaks running along
    /// the x axis, one row <see cref="MaterialStrength.StreakRowDp"/> deep. A heading underline, a tab's label.
    /// </summary>
    public static Paint Marker(Paint paint, Color ink, float canvasUnitsPerDp, bool pressed = false)
    {
        paint.AntiAlias = true;
        paint.Color = ink;
        paint.SetShader(MaterialShaders.Tiled(
            pressed ? MaterialShaders.MarkerStreaksPressed : MaterialShaders.MarkerStreaks,
            MaterialStrength.StreakRowDp * canvasUnitsPerDp));
        return paint;
    }

    /// <summary>The Android path for a glyph's data, built once.</summary>
    public static Path PathFor(string data)
    {
        if (Paths.TryGetValue(data, out var cached))
        {
            return cached;
        }

        var path = new Path();
        foreach (var command in PathData.Parse(data))
        {
            switch (command.Verb)
            {
                case PathVerb.Move:
                    path.MoveTo(command.X, command.Y);
                    break;
                case PathVerb.Line:
                    path.LineTo(command.X, command.Y);
                    break;
                case PathVerb.Cubic:
                    path.CubicTo(command.X1, command.Y1, command.X2, command.Y2, command.X, command.Y);
                    break;
                case PathVerb.Close:
                    path.Close();
                    break;
            }
        }
        Paths[data] = path;
        return path;
    }
}
