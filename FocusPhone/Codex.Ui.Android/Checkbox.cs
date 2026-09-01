using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Domain.Design;

// Android.Graphics.Path, not System.IO.Path — the implicit usings bring both in and
// the ambiguity is a compile error, not a warning.
using Path = Android.Graphics.Path;

namespace Codex.Ui;

/// <summary>
/// The §7 checkbox: a hairline square at rest, filled in the functional hue with an
/// ink-on-hue check when picked.
///
/// Drawn rather than assembled from a stock CheckBox because the platform control
/// brings Material's own colours, ripple and metrics — three things the paper
/// surface has opinions about — and the box is four lines of canvas.
///
/// It is not clickable itself: the row it sits in is the target, which is what keeps
/// the ≥48dp hit-area §7 asks for without a 48dp box crowding the title.
/// </summary>
public sealed class Checkbox : View
{
    private readonly Paint stroke = new() { AntiAlias = true };
    private readonly Paint fill = new() { AntiAlias = true };
    private readonly Paint tick = new() { AntiAlias = true };
    private bool picked;

    public Checkbox(Context context) : base(context)
    {
        stroke.SetStyle(Paint.Style.Stroke);
        stroke.StrokeWidth = PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp);
        stroke.Color = PaperTheme.Hairline;

        fill.SetStyle(Paint.Style.Fill);
        fill.Color = PaperTheme.Accent;

        tick.SetStyle(Paint.Style.Stroke);
        tick.StrokeWidth = PaperTheme.Dp(context, 2);
        tick.StrokeCap = Paint.Cap.Round;
        tick.Color = PaperTheme.Paper;
    }

    public void SetPicked(bool value)
    {
        if (picked == value)
        {
            return;
        }

        picked = value;
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);

        var inset = stroke.StrokeWidth / 2f;
        var radius = PaperTheme.Dp(Context!, DesignTokens.Shape.HairlineDp) * 3f;
        var box = new RectF(inset, inset, Width - inset, Height - inset);

        if (!picked)
        {
            canvas.DrawRoundRect(box, radius, radius, stroke);
            return;
        }

        canvas.DrawRoundRect(box, radius, radius, fill);

        // A check, not a glyph: at 20dp a typeface tick renders as a smudge.
        var path = new Path();
        path.MoveTo(Width * 0.24f, Height * 0.52f);
        path.LineTo(Width * 0.43f, Height * 0.71f);
        path.LineTo(Width * 0.77f, Height * 0.31f);
        canvas.DrawPath(path, tick);
    }
}
