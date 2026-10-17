using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Domain.Design;
using Codex.Ui;

namespace Constitution.Waiting;

/// <summary>
/// The corner fold, drawn from <see cref="DogEarGeometry"/>: the same two triangles the suite's own
/// fold draws (the hole in the role's colour, the flap lying back across the page, a hairline
/// crease), with the back-turn arrow on an undo only. It is drawn here rather than borrowed because
/// <c>Codex.Ui.DogEar</c> opens the shared Waiting page on a tap, and Constitution's Waiting page is
/// its own, written onto the crinkled ground in this app's frame.
/// </summary>
internal sealed class FoldView : View
{
    // How many straight segments the arrow's arc is flattened to; at 40dp indistinguishable from the curve.
    private const int ArrowSegments = 32;

    private readonly float density;
    private readonly Color hue;
    private readonly bool showsBackArrow;

    public FoldView(Context context, string hint, FoldRole role, bool showsBackArrow)
        : base(context)
    {
        density = context.Resources!.DisplayMetrics!.Density;
        hue = HueOf(role);
        this.showsBackArrow = showsBackArrow;
        Clickable = true;
        ContentDescription = hint;
    }

    /// <summary>The suite's colour for a fold role, by the names <see cref="DogEar.Hues"/> already carries.</summary>
    public static Color HueOf(FoldRole role) => role switch
    {
        FoldRole.Problem => DogEar.Hues.Problem,
        FoldRole.Caution => DogEar.Hues.Caution,
        FoldRole.Action => DogEar.Hues.Action,
        FoldRole.Resolved => DogEar.Hues.Resolved,
        FoldRole.Reference => DogEar.Hues.Reference,
        FoldRole.Progress => DogEar.Hues.Progress,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "This fold role has no colour."),
    };

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        float w = Width, h = Height;

        // The hole: the part of the page that is no longer there, showing what the fold carries.
        using var underside = new Paint { AntiAlias = true, Color = hue };
        using var hole = new Android.Graphics.Path();
        hole.MoveTo(w, 0);
        hole.LineTo(0, 0);
        hole.LineTo(w, h);
        hole.Close();
        canvas.DrawPath(hole, underside);

        // The flap: the corner turned over and lying back across the page, its loose tip lifted off it.
        using var flap = new Android.Graphics.Path();
        flap.MoveTo(0, 0);
        flap.LineTo(w, h);
        flap.LineTo(
            DogEarGeometry.Scale(DogEarGeometry.FlapTip.X, w),
            DogEarGeometry.Scale(DogEarGeometry.FlapTip.Y, h));
        flap.Close();
        using var back = new Paint { AntiAlias = true, Color = PaperTheme.PaperDeep };
        canvas.DrawPath(flap, back);

        using var crease = new Paint { AntiAlias = true, StrokeWidth = density, Color = PaperTheme.Hairline };
        crease.SetStyle(Paint.Style.Stroke);
        canvas.DrawLine(0, 0, w, h, crease);

        if (!showsBackArrow)
        {
            return;
        }

        using var mark = new Paint(PaintFlags.AntiAlias)
        {
            Color = PaperTheme.PaperBright,
            StrokeWidth = DogEarGeometry.Scale(DogEarGeometry.ArrowStroke, w),
            StrokeCap = Paint.Cap.Round,
            StrokeJoin = Paint.Join.Round,
        };
        mark.SetStyle(Paint.Style.Stroke);

        using var arc = new Android.Graphics.Path();
        var points = DogEarGeometry.ArrowArc(ArrowSegments);
        arc.MoveTo(DogEarGeometry.Scale(points[0].X, w), DogEarGeometry.Scale(points[0].Y, h));
        for (var i = 1; i < points.Count; i++)
        {
            arc.LineTo(DogEarGeometry.Scale(points[i].X, w), DogEarGeometry.Scale(points[i].Y, h));
        }

        canvas.DrawPath(arc, mark);

        using var head = new Android.Graphics.Path();
        var wings = DogEarGeometry.ArrowHead;
        head.MoveTo(DogEarGeometry.Scale(wings[0].X, w), DogEarGeometry.Scale(wings[0].Y, h));
        head.LineTo(DogEarGeometry.Scale(wings[1].X, w), DogEarGeometry.Scale(wings[1].Y, h));
        head.LineTo(DogEarGeometry.Scale(wings[2].X, w), DogEarGeometry.Scale(wings[2].Y, h));
        canvas.DrawPath(head, mark);
    }
}
