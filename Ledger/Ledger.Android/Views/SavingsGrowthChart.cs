using System.Globalization;
using Microsoft.Maui.Graphics;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// D81/D113, mockup 9's second frame: one savings account's monthly growth — the recorded balance as
/// a solid line, a DOTTED projection from now to the goal (only when a goal is set and reaches one),
/// and the goal itself as a dashed reference line, labelled on the chart. No legend: the title above
/// the chart names the account (<see cref="SavingsPage"/> draws that part; this view is only the plot).
///
/// <para><b>D113: "in ink" — the third chart colour</b> after Snowball's green bars
/// (<see cref="SnowballChartPalette.RampHex"/>) and its fixed ink-blue
/// (<see cref="SnowballChartPalette.InkBlueHex"/>). Drawn in <c>PaperResources</c>'s plain <c>Ink</c>
/// role token, never Savings' own tab hue (<c>Pages.LedgerAreas</c>'s slate-blue) — the same "no
/// control wears its own tab's role hue" rule <c>AreaHueTests</c> already holds Index to elsewhere.
/// Nothing here reads <c>LedgerAreas</c> at all, which is what makes that impossible to get wrong by
/// accident rather than merely correct by care.</para>
/// </summary>
public sealed class SavingsGrowthChart : GraphicsView
{
    private const float ChartHeight = 130f;
    private const float NoGoalChartHeight = 56f;
    private const float TopPad = 14f;
    private const float BottomPad = 18f;

    private readonly Painter painter = new();

    public SavingsGrowthChart()
    {
        Drawable = painter;
        SizeChanged += (_, _) => Invalidate();
    }

    public void SetSeries(SavingsAccountGrowth series)
    {
        painter.Series = series;
        HeightRequest = series.GoalTargetAmount is null ? NoGoalChartHeight : ChartHeight;
        Invalidate();
    }

    private sealed class Painter : IDrawable
    {
        public SavingsAccountGrowth? Series { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Series is not { Points.Count: > 0 } series)
            {
                return;
            }

            var ink = PaperResources.Colour("Ink");
            var softInk = PaperResources.Colour("InkSoft");
            var hairline = PaperResources.Colour("Hairline");

            var latest = series.Points[^1].Balance;

            // No goal (mockup 9's own note): "a plain line and its latest balance" — no axis, no
            // dashed reference, no projection, just the shape of the growth and where it stands now.
            if (series.GoalTargetAmount is not { } goal)
            {
                DrawPlainLine(canvas, dirtyRect, series, ink, softInk, latest);
                return;
            }

            // A goal already met has nothing left to project (the card and post-it say "Goal reached").
            var reachesDate = series.ReachesIt is { IsNotAtThisPace: false, Date: { } projectedDate }
                && !SavingsGoalCopy.IsReached(latest, goal)
                ? projectedDate
                : (DateOnly?)null;

            var plotTop = TopPad;
            var plotBottom = dirtyRect.Height - BottomPad;
            var plotHeight = plotBottom - plotTop;

            // The scale runs from $0 to whichever is larger: the goal, or the highest recorded balance
            // (a goal already exceeded must not clip the very line that proves it).
            var maxValue = Math.Max((double)goal, (double)series.Points.Max(p => p.Balance));
            if (maxValue <= 0)
            {
                return;
            }

            float YFor(decimal value) => plotBottom - (float)((double)value / maxValue) * plotHeight;

            // The goal: a dashed reference line, labelled (mockup 9: "the goal as a dashed reference
            // line labelled on the chart").
            var goalY = YFor(goal);
            canvas.StrokeSize = 1.5f;
            canvas.StrokeColor = ink;
            canvas.StrokeDashPattern = [4, 3];
            canvas.DrawLine(0, goalY, dirtyRect.Width, goalY);
            canvas.StrokeDashPattern = null;

            canvas.FontSize = 10.5f;
            canvas.FontColor = ink;
            canvas.DrawString(
                $"Goal {Money(goal)}", 0, Math.Max(0, goalY - 14), dirtyRect.Width, 14,
                HorizontalAlignment.Left, VerticalAlignment.Top);

            // The baseline.
            canvas.StrokeSize = 1f;
            canvas.StrokeColor = hairline;
            canvas.DrawLine(0, plotBottom, dirtyRect.Width, plotBottom);

            // The recorded balance: a solid line across the account's whole recorded history.
            var months = series.Points.Count;
            var firstMonth = series.Points[0].MonthOf;
            var lastMonth = series.Points[^1].MonthOf;
            var endMonth = reachesDate ?? lastMonth;

            // R-G21: the first recorded month is the origin and the plot's end is the full width; the
            // mapping is GrowthChartScale's, so it is tested rather than re-derived here.
            float XFor(DateOnly month) =>
                (float)GrowthChartScale.FractionAcross(month, firstMonth, endMonth) * dirtyRect.Width;

            var path = new PathF();
            for (var i = 0; i < months; i++)
            {
                var x = XFor(series.Points[i].MonthOf);
                var y = YFor(series.Points[i].Balance);
                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }
            canvas.StrokeSize = 2f;
            canvas.StrokeColor = ink;
            canvas.StrokeDashPattern = null;
            canvas.DrawPath(path);

            var nowX = XFor(lastMonth);
            var nowY = YFor(latest);
            canvas.FillColor = ink;
            canvas.FillCircle(nowX, nowY, 4);
            canvas.Font = new Microsoft.Maui.Graphics.Font("SansSemibold", FontWeights.SemiBold, FontStyleType.Normal);
            canvas.DrawString("Now", nowX + 6, nowY - 12, dirtyRect.Width - nowX - 6, 14,
                HorizontalAlignment.Left, VerticalAlignment.Top);

            // The projection: DOTTED from now to the goal (mockup 9: "the projection dotted from now
            // to the goal") — only drawn when the pace actually gets there (D82/D83's "not at this
            // pace" has nothing to project).
            if (reachesDate is { } date)
            {
                var goalX = XFor(date);
                canvas.StrokeSize = 2f;
                canvas.StrokeColor = ink;
                canvas.StrokeDashPattern = [1.5f, 4];
                var projected = new PathF();
                projected.MoveTo(nowX, nowY);
                projected.LineTo(goalX, goalY);
                canvas.DrawPath(projected);
                canvas.StrokeDashPattern = null;

                canvas.Font = new Microsoft.Maui.Graphics.Font("SansSemibold", FontWeights.SemiBold, FontStyleType.Normal);
                // Right-aligned INSIDE the plot, ending at the goal's x (mockup 9): the box starts to the
                // left of goalX, not at it, or the label is drawn off the chart at the right edge.
                var labelWidth = Math.Min(80f, goalX);
                canvas.DrawString($"~{date:MMM yyyy}", goalX - labelWidth, Math.Max(0, goalY - 14), labelWidth, 14,
                    HorizontalAlignment.Right, VerticalAlignment.Top);
            }

            DrawMonthLabels(canvas, dirtyRect.Width, plotBottom + 4, softInk, firstMonth, lastMonth);
        }

        private static void DrawPlainLine(
            ICanvas canvas, RectF dirtyRect, SavingsAccountGrowth series, Color ink, Color softInk, decimal latest)
        {
            var plotBottom = dirtyRect.Height - 4;
            canvas.StrokeSize = 1f;
            canvas.StrokeColor = PaperResources.Colour("Hairline");
            canvas.DrawLine(0, plotBottom, dirtyRect.Width, plotBottom);

            canvas.FontSize = 10.5f;
            canvas.FontColor = ink;
            canvas.DrawString(Money(latest), dirtyRect.Width - 90, 0, 90, 14,
                HorizontalAlignment.Right, VerticalAlignment.Top);

            if (series.Points.Count < 2)
            {
                return;
            }

            var maxValue = (double)series.Points.Max(p => p.Balance);
            if (maxValue <= 0)
            {
                return;
            }

            var firstMonth = series.Points[0].MonthOf;
            var lastMonth = series.Points[^1].MonthOf;

            var path = new PathF();
            for (var i = 0; i < series.Points.Count; i++)
            {
                var x = (float)GrowthChartScale.FractionAcross(series.Points[i].MonthOf, firstMonth, lastMonth) * dirtyRect.Width;
                var y = plotBottom - (float)((double)series.Points[i].Balance / maxValue) * (plotBottom - 4);
                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }
            canvas.StrokeSize = 2f;
            canvas.StrokeColor = ink;
            canvas.DrawPath(path);
        }

        private static void DrawMonthLabels(
            ICanvas canvas, float width, float labelY, Color softInk, DateOnly first, DateOnly last)
        {
            canvas.FontSize = 9;
            canvas.FontColor = softInk;
            canvas.Font = new Microsoft.Maui.Graphics.Font("Sans", FontWeights.Regular, FontStyleType.Normal);
            canvas.DrawString(first.ToString("MMM ''yy", CultureInfo.InvariantCulture), 0, labelY, 60, 12,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            canvas.DrawString(last.ToString("MMM ''yy", CultureInfo.InvariantCulture), width - 60, labelY, 60, 12,
                HorizontalAlignment.Right, VerticalAlignment.Top);
        }

        private static string Money(decimal value) =>
            value.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
    }
}
