using Microsoft.Maui.Graphics;

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
}
