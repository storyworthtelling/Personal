using Android.Content;
using Android.Graphics;
using Storyworthtelling.Materials.Android;
using Android.Views;
using Android.Views.Accessibility;
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
        // §2 Colored pencil: the box, its fill and the tick are all drawn with the pencil's tooth.
        var density = context.Resources!.DisplayMetrics!.Density;
        MaterialPainters.Pencil(stroke, PaperTheme.InkMuted, density);
        stroke.SetStyle(Paint.Style.Stroke);
        stroke.StrokeWidth = PaperUi.PencilLineDp * density;

        MaterialPainters.Pencil(fill, PaperTheme.Accent, density);
        fill.SetStyle(Paint.Style.Fill);

        MaterialPainters.Pencil(tick, PaperTheme.Paper, density);
        tick.SetStyle(Paint.Style.Stroke);
        tick.StrokeWidth = PaperTheme.Dp(context, 2);
        tick.StrokeCap = Paint.Cap.Round;
        Describe();
    }

    // A drawn box says nothing to TalkBack by itself (§17, audit 10 §6): it is a checkable thing with a state, and the
    // row it sits in reads that state with its own words.
    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null)
        {
            return;
        }

        info.ClassName = "android.widget.CheckBox";
        info.Checkable = true;
        info.Checked = picked;
    }

    private void Describe()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            StateDescription = picked ? "Checked" : "Not checked";
        }
    }

    public void SetPicked(bool value)
    {
        if (picked == value)
        {
            return;
        }

        picked = value;
        Describe();
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
