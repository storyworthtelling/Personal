using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Body;

namespace Constitution.BodyTab;

/// <summary>
/// One signal's line, drawn on the chart paper the scrap already supplies (book page 16): a solid neutral
/// line for each run of days, a faint dotted bridge across days with no reading, a dot on the latest, and, for
/// a bed that has gone quiet, a faint dotted run from the last reading to the right edge, so the empty days read
/// as waiting rather than as a line that stopped. The ink is the page's own ink and nothing else: there is no
/// colour for better or worse, no overlay and no second line, because the geometry it draws from carries none
/// (<see cref="ChartGeometry"/>). Transparent, so the paper's grid shows through.
/// </summary>
internal sealed class BodyChartView : View
{
    // From the mockup's SVG: 300 wide with the line drawn from x 4 to x 290.
    private const float LeftPadDp = 4;
    private const float RightPadDp = 10;
    private const float LineDp = 1.6f;
    private const float BridgeDp = 1.2f;
    private const float DotRadiusDp = 2.8f;
    private const float DashOnDp = 1.5f;
    private const float DashOffDp = 3;

    private readonly IReadOnlyList<BodyReading> readings;
    private readonly BodyWindow window;
    private readonly DateOnly today;
    private readonly int heightDp;
    private readonly bool continuesDotted;
    private readonly float scale;
    private readonly Paint line;
    private readonly Paint dotted;
    private readonly Paint dot;

    public BodyChartView(
        Context context, IReadOnlyList<BodyReading> readings, BodyWindow window, DateOnly today, int heightDp, bool continuesDotted,
        float lineScale = 1)
        : base(context)
    {
        this.readings = readings;
        this.window = window;
        this.today = today;
        this.heightDp = heightDp;
        this.continuesDotted = continuesDotted;
        scale = Context!.Resources!.DisplayMetrics!.Density;

        line = Stroke(PaperTheme.Ink, LineDp * lineScale);
        line.StrokeJoin = Paint.Join.Round;
        line.StrokeCap = Paint.Cap.Round;
        dotted = Stroke(PaperTheme.InkMuted, BridgeDp * lineScale);
        dotted.SetPathEffect(new DashPathEffect([DashOnDp * scale, DashOffDp * scale], 0));
        dot = new Paint(PaintFlags.AntiAlias);
        dot.Color = PaperTheme.Ink;
        ContentDescription = "A chart of the readings";
    }

    private Paint Stroke(Color ink, float widthDp)
    {
        var paint = new Paint(PaintFlags.AntiAlias) { StrokeWidth = widthDp * scale };
        paint.SetStyle(Paint.Style.Stroke);
        paint.Color = ink;
        return paint;
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec) =>
        SetMeasuredDimension(MeasureSpec.GetSize(widthMeasureSpec), PaperTheme.Dp(Context!, heightDp));

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var left = LeftPadDp * scale;
        var width = Width - ((LeftPadDp + RightPadDp) * scale);
        if (width <= 0 || Height <= 0)
        {
            return;
        }

        var geometry = ChartGeometry.Compute(readings, window, today, width, Height);
        foreach (var run in geometry.Lines)
        {
            using var path = new Android.Graphics.Path();
            path.MoveTo(left + (float)run[0].X, (float)run[0].Y);
            foreach (var point in run.Skip(1))
            {
                path.LineTo(left + (float)point.X, (float)point.Y);
            }

            canvas.DrawPath(path, line);
        }

        foreach (var bridge in geometry.Bridges)
        {
            canvas.DrawLine(
                left + (float)bridge.From.X, (float)bridge.From.Y, left + (float)bridge.To.X, (float)bridge.To.Y, dotted);
        }

        if (geometry.Dot is not { } latest)
        {
            return;
        }

        if (continuesDotted && latest.X < width)
        {
            canvas.DrawLine(left + (float)latest.X, (float)latest.Y, left + width, (float)latest.Y, dotted);
        }

        canvas.DrawCircle(left + (float)latest.X, (float)latest.Y, DotRadiusDp * scale, dot);
    }
}
