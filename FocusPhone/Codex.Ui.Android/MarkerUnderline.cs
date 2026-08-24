using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// The stroke under every screen header (DESIGN.md §4): a gently curved marker
/// line rising from the left, in the section's functional or identity hue.
///
/// Two passes, because a real marker does not lay down flat ink — a full-strength
/// stroke with a lighter, slightly lower one trailing it, which reads as a nib that
/// was drawn once rather than a rule that was ruled. Nothing here is a shadow; §5
/// bans those. The curve is proportional to the width, so it looks drawn at any
/// measure rather than stretched.
/// </summary>
public class MarkerUnderline : View
{
    /// <summary>The band the stroke is drawn in. Tall enough for both passes.</summary>
    public const int HeightDp = 12;

    private readonly Paint stroke;
    private readonly Paint trail;
    private readonly Path path = new();
    private readonly Path trailPath = new();

    public MarkerUnderline(Context context, Color hue) : base(context)
    {
        stroke = new Paint
        {
            AntiAlias = true,
            Color = hue,
            StrokeWidth = PaperTheme.Dp(context, 5),
            StrokeCap = Paint.Cap.Round,
            Alpha = 235,
        };
        stroke.SetStyle(Paint.Style.Stroke);

        trail = new Paint
        {
            AntiAlias = true,
            Color = hue,
            StrokeWidth = PaperTheme.Dp(context, 2),
            StrokeCap = Paint.Cap.Round,
            Alpha = 105,
        };
        trail.SetStyle(Paint.Style.Stroke);
    }

    /// <summary>Re-ink the stroke — the hue follows the area you are in.</summary>
    public void SetHue(Color hue)
    {
        stroke.Color = hue;
        stroke.Alpha = 235;
        trail.Color = hue;
        trail.Alpha = 105;
        Invalidate();
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        SetMeasuredDimension(
            ResolveSize(SuggestedMinimumWidth, widthMeasureSpec),
            PaperTheme.Dp(Context!, HeightDp));
    }

    protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
    {
        base.OnSizeChanged(width, height, oldWidth, oldHeight);
        Rebuild(width, height);
    }

    // Proportions taken from the approved mockups: the stroke starts low on the
    // left and lifts about two thirds of the band by the right edge.
    private void Rebuild(float width, float height)
    {
        var inset = stroke.StrokeWidth / 2f;
        var left = inset;
        var right = Math.Max(left, width - inset);

        var low = height * 0.72f;
        var high = height * 0.20f;

        path.Reset();
        path.MoveTo(left, low);
        path.CubicTo(width * 0.20f, low * 0.94f, width * 0.50f, high * 1.7f, right, high);

        trailPath.Reset();
        trailPath.MoveTo(left, low + inset * 0.5f);
        trailPath.CubicTo(width * 0.23f, low, width * 0.53f, high * 2.1f, right, high * 1.9f);
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        canvas.DrawPath(trailPath, trail);
        canvas.DrawPath(path, stroke);
    }
}
