using Android.Content;
using Android.Graphics;
using Path = Android.Graphics.Path;
using Android.Views;
using Android.Widget;
using Constitution.Domain.Ground;
using Constitution.Domain.Scrapbook;
using Storyworthtelling.Paper;

namespace Constitution.Scrapbook;

/// <summary>
/// The crinkled exam-table ground (D43, book p.11), drawn behind a page's well and nothing else.
/// The view IS the well, so the binder gutter beside it is never touched, and it draws no text:
/// creases, ticks and the one perforated tear line, from <see cref="CrinkleField"/> and nowhere
/// else. The paper is the suite's own (the window's); every mark is a registered ink at a
/// registered alpha, so the ground is marks on paper and not a second surface.
/// </summary>
public sealed class CrinkleGroundView : FrameLayout
{
    private readonly int seed;
    private readonly Paint crease = ScrapInk.Stroke(ScrapInk.CreaseInk, (float)MaterialTokens.CreaseStrokeWidth, ScrapInk.Alpha(MaterialTokens.CreaseAlpha));
    private readonly Paint tick = TickPaint();
    private readonly Paint perforation = PerforationPaint();
    private CrinkleField? field;

    public CrinkleGroundView(Context context, int seed, View well) : base(context)
    {
        this.seed = seed;
        SetWillNotDraw(false);
        AddView(well, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    private float Density => Context!.Resources!.DisplayMetrics!.Density;

    protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
    {
        base.OnSizeChanged(width, height, oldWidth, oldHeight);
        var density = Density;
        field = width > 0 && height > 0
            ? CrinkleField.ForWell(seed, (int)Math.Round(width / density), (int)Math.Round(height / density))
            : null;
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (field is null)
        {
            return;
        }

        var density = Density;
        canvas.Save();
        canvas.Scale(density, density);

        foreach (var line in field.Creases)
        {
            var path = new Path();
            path.MoveTo((float)line.Points[0].X, (float)line.Points[0].Y);
            for (var i = 1; i < line.Points.Count; i++)
            {
                path.LineTo((float)line.Points[i].X, (float)line.Points[i].Y);
            }

            canvas.DrawPath(path, crease);
        }

        foreach (var mark in field.Ticks)
        {
            tick.Alpha = ScrapInk.Alpha((decimal)CrinkleInk.Map(
                mark.Intensity, (double)MaterialTokens.TickAlphaMin, (double)MaterialTokens.TickAlphaMax));
            canvas.DrawLine((float)mark.A.X, (float)mark.A.Y, (float)mark.B.X, (float)mark.B.Y, tick);
        }

        // The one tear line, across the whole well (the CSS's `.perf` runs edge to gutter).
        var tear = (float)(field.Perforation.YFraction * Height / density);
        canvas.DrawLine(0, tear, Width / density, tear, perforation);

        canvas.Restore();
    }

    private static Paint TickPaint() =>
        ScrapInk.Stroke(ScrapInk.TickInk, (float)MaterialTokens.TickStrokeWidth);

    /// <summary>
    /// A 1.5 dp dotted line, the CSS's `border-top: 1.5px dotted`: round dots of the line's width,
    /// a gap of the same width between them, so a dot every two widths.
    /// </summary>
    private static Paint PerforationPaint()
    {
        var width = (float)MaterialTokens.PerforationWidth;
        var paint = ScrapInk.Stroke(ScrapInk.PerforationInk, width, ScrapInk.Alpha(MaterialTokens.PerforationAlpha));
        paint.StrokeCap = Paint.Cap.Round;
        const float dot = 0.01f;
        paint.SetPathEffect(new DashPathEffect([dot, (2 * width) - dot], 0));
        return paint;
    }
}
