using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Platform;
using Storyworthtelling.Materials.Android;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// The §7 checkbox: a hairline square at rest, filled in the action hue with a paper-on-hue check
/// when picked.
///
/// <para>Drawn rather than assembled from MAUI's <c>CheckBox</c> because the platform control
/// brings Material's own colours, ripple and metrics — three things the paper surface has opinions
/// about — and the box is four lines of canvas.</para>
///
/// <para><b>It is not tappable itself.</b> The row it sits in is the target, which is what keeps
/// the ≥48dp hit-area §7 asks for without a 48dp box crowding the title.</para>
/// </summary>
public sealed class CheckboxDrawable : IDrawable
{
    private readonly Func<string, Color> token;

    public CheckboxDrawable(Func<string, Color> token)
    {
        this.token = token;
    }

    public bool Picked { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (canvas is PlatformCanvas platform && platform.Canvas is { } native)
        {
            DrawInPencil(native, dirtyRect);
            return;
        }

        canvas.Antialias = true;

        float stroke = DesignTokens.Shape.HairlineDp;
        var inset = stroke / 2f;
        var radius = DesignTokens.Shape.HairlineDp * 3f;
        float w = dirtyRect.Width, h = dirtyRect.Height;

        if (!Picked)
        {
            canvas.StrokeColor = token("Hairline");
            canvas.StrokeSize = stroke;
            canvas.DrawRoundedRectangle(inset, inset, w - stroke, h - stroke, radius);
            return;
        }

        canvas.FillColor = token("ActionMark");
        canvas.FillRoundedRectangle(inset, inset, w - stroke, h - stroke, radius);

        // A check, not a glyph: at 20dp a typeface tick renders as a smudge.
        var tick = new PathF();
        tick.MoveTo(w * 0.24f, h * 0.52f);
        tick.LineTo(w * 0.43f, h * 0.71f);
        tick.LineTo(w * 0.77f, h * 0.31f);

        canvas.StrokeColor = token("Paper");
        canvas.StrokeSize = 2;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawPath(tick);
    }

    // §2 Colored pencil: the box, its fill and the tick with the pencil's tooth, as the native kit's checkbox. The
    // GraphicsView's canvas is in dp, so one dp is one canvas unit.
    private readonly global::Android.Graphics.Paint box = new();
    private readonly global::Android.Graphics.Paint face = new();
    private readonly global::Android.Graphics.Paint mark = new();

    private void DrawInPencil(global::Android.Graphics.Canvas native, RectF rect)
    {
        var line = PaperMaterials.PencilLineDp;
        var radius = DesignTokens.Shape.HairlineDp * 3f;
        var area = new global::Android.Graphics.RectF(rect.Left + (line / 2), rect.Top + (line / 2), rect.Right - (line / 2), rect.Bottom - (line / 2));

        if (!Picked)
        {
            MaterialPainters.Pencil(box, token("InkMuted").ToPlatform(), 1f);
            box.SetStyle(global::Android.Graphics.Paint.Style.Stroke);
            box.StrokeWidth = line;
            native.DrawRoundRect(area, radius, radius, box);
            return;
        }

        MaterialPainters.Pencil(face, token("ActionMark").ToPlatform(), 1f);
        face.SetStyle(global::Android.Graphics.Paint.Style.Fill);
        native.DrawRoundRect(area, radius, radius, face);

        var tick = new global::Android.Graphics.Path();
        tick.MoveTo(rect.Left + (rect.Width * 0.24f), rect.Top + (rect.Height * 0.52f));
        tick.LineTo(rect.Left + (rect.Width * 0.43f), rect.Top + (rect.Height * 0.71f));
        tick.LineTo(rect.Left + (rect.Width * 0.77f), rect.Top + (rect.Height * 0.31f));
        MaterialPainters.Pencil(mark, token("Paper").ToPlatform(), 1f);
        mark.SetStyle(global::Android.Graphics.Paint.Style.Stroke);
        mark.StrokeWidth = 2;
        mark.StrokeCap = global::Android.Graphics.Paint.Cap.Round;
        native.DrawPath(tick, mark);
    }
}
