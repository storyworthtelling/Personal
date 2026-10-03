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
///
/// <para><b>Axes (Phase 4, mockup 9 frame 2):</b> dashed hairlines at round money steps
/// (<see cref="GrowthChartAxis.MoneySteps"/>) labelled in a mono gutter to the right of the plot; the time axis
/// labelled once a year from the first recorded month (<see cref="GrowthChartAxis.YearTicks"/>); the projected
/// end of the dotted line marked with a HOLLOW circle — filled with the paper, ringed in ink — against the
/// solid dot of "Now". Every label's size is the Label step of the type ladder, never a literal.</para>
/// </summary>
public sealed class SavingsGrowthChart : GraphicsView
{
    private const float ChartHeight = 150f;
    private const float NoGoalChartHeight = 64f;
    private const float TopPad = 20f;
    private const float BottomPad = 24f;

    // Beside the plot, for the steps' labels ("15k") and, with no goal, the latest balance ("$1,800").
    private const float StepGutter = 40f;
    private const float BalanceGutter = 60f;

    private const float MarkerRadius = 4f;

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

        private static Microsoft.Maui.Graphics.Font Face(string alias, int weight) =>
            new(alias, weight, FontStyleType.Normal);

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Series is not { Points.Count: > 0 } series)
            {
                return;
            }

            var ink = PaperResources.Colour("Ink");
            var softInk = PaperResources.Colour("InkSoft");
            var paper = PaperResources.Colour("Paper");
            var fontSize = (float)PaperResources.Token<double>("LabelSize");
            var lineHeight = fontSize * 1.4f;

            var latest = series.Points[^1].Balance;

            // No goal (mockup 9's own note): "a plain line and its latest balance" — no axis, no
            // dashed reference, no projection, just the shape of the growth and where it stands now.
            if (series.GoalTargetAmount is not { } goal)
            {
                DrawPlainLine(canvas, dirtyRect, series, ink, fontSize, latest);
                return;
            }

            // A goal already met has nothing left to project (the card and post-it say "Goal reached").
            var reachesDate = series.ReachesIt is { IsNotAtThisPace: false, Date: { } projectedDate }
                && !SavingsGoalCopy.IsReached(latest, goal)
                ? projectedDate
                : (DateOnly?)null;

            var plotWidth = dirtyRect.Width - StepGutter;
            var plotTop = TopPad;
            var plotBottom = dirtyRect.Height - BottomPad;
            var plotHeight = plotBottom - plotTop;

            // The scale runs from $0 to whichever is larger: the goal, or the highest recorded balance
            // (a goal already exceeded must not clip the very line that proves it).
            var topOfScale = Math.Max(goal, series.Points.Max(p => p.Balance));
            var maxValue = (double)topOfScale;
            if (maxValue <= 0)
            {
                return;
            }

            float YFor(decimal value) => plotBottom - (float)((double)value / maxValue) * plotHeight;

            canvas.FontSize = fontSize;

            // The y axis: dashed hairlines at round steps, each labelled in the gutter (mockup 9).
            foreach (var step in GrowthChartAxis.MoneySteps(topOfScale))
            {
                var stepY = YFor(step);
                canvas.StrokeSize = 1f;
                canvas.StrokeColor = PaperResources.Colour("InkMuted").WithAlpha(0.35f);
                canvas.StrokeDashPattern = [2f, 3f];
                canvas.DrawLine(0, stepY, plotWidth, stepY);
                canvas.StrokeDashPattern = null;

                canvas.Font = Face("Mono", FontWeights.Regular);
                canvas.FontColor = softInk;
                canvas.DrawString(
                    ChartAxis.MoneyTick(step), plotWidth + 4, stepY - lineHeight / 2f, StepGutter - 4, lineHeight,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }

            // The goal: a dashed reference line, labelled (mockup 9: "the goal as a dashed reference
            // line labelled on the chart").
            var goalY = YFor(goal);
            canvas.StrokeSize = 1f;
            canvas.StrokeColor = ink;
            canvas.StrokeDashPattern = [6, 4];
            canvas.DrawLine(0, goalY, plotWidth, goalY);
            canvas.StrokeDashPattern = null;

            canvas.Font = Face("SansSemibold", FontWeights.SemiBold);
            canvas.FontColor = ink;
            canvas.DrawString(
                $"Goal {PlanMoney.Whole(goal)}", 0, Math.Max(0, goalY - lineHeight - 1), plotWidth, lineHeight,
                HorizontalAlignment.Left, VerticalAlignment.Top);

            // The baseline.
            canvas.StrokeSize = 1f;
            canvas.StrokeColor = softInk;
            canvas.DrawLine(0, plotBottom, plotWidth, plotBottom);

            // The recorded balance: a solid line across the account's whole recorded history.
            var months = series.Points.Count;
            var firstMonth = series.Points[0].MonthOf;
            var lastMonth = series.Points[^1].MonthOf;
            var endMonth = reachesDate ?? lastMonth;

            // R-G21: the first recorded month is the origin and the plot's end is the full plot width; the
            // mapping is GrowthChartScale's, so it is tested rather than re-derived here.
            float XFor(DateOnly month) =>
                (float)GrowthChartScale.FractionAcross(month, firstMonth, endMonth) * plotWidth;

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

                // Where the line is going, not where it has been: a HOLLOW marker — the paper inside, ink around.
                canvas.FillColor = paper;
                canvas.FillCircle(goalX, goalY, MarkerRadius);
                canvas.StrokeSize = 2f;
                canvas.StrokeColor = ink;
                canvas.DrawCircle(goalX, goalY, MarkerRadius);

                canvas.Font = Face("SansSemibold", FontWeights.SemiBold);
                canvas.FontColor = ink;
                // Right-aligned INSIDE the plot, ending just left of the marker (mockup 9): the box starts to
                // the left of goalX, not at it, or the label is drawn off the chart at the right edge.
                var labelWidth = Math.Min(96f, goalX);
                canvas.DrawString(GrowthChartAxis.GoalEndLabel(date), goalX - MarkerRadius - 2 - labelWidth, Math.Max(0, goalY - lineHeight - 1), labelWidth, lineHeight,
                    HorizontalAlignment.Right, VerticalAlignment.Top);
            }

            // "Now": the solid dot, ringed in the paper, drawn after the projection so it sits on top of it.
            canvas.FillColor = ink;
            canvas.FillCircle(nowX, nowY, MarkerRadius);
            canvas.StrokeSize = 2f;
            canvas.StrokeColor = paper;
            canvas.DrawCircle(nowX, nowY, MarkerRadius);
            canvas.Font = Face("SansSemibold", FontWeights.SemiBold);
            canvas.FontColor = ink;
            // Across to the chart's right edge, gutter included: with nothing projected the dot sits on the plot's
            // right edge, and a width measured from the plot would be none at all.
            canvas.DrawString("Now", nowX + GrowthChartAxis.NowLabelGap, nowY + 2, GrowthChartAxis.NowLabelWidth(dirtyRect.Width, nowX), lineHeight,
                HorizontalAlignment.Left, VerticalAlignment.Top);

            DrawYearLabels(canvas, plotWidth, plotBottom + 4, softInk, fontSize, firstMonth, endMonth);
        }

        private static void DrawPlainLine(
            ICanvas canvas, RectF dirtyRect, SavingsAccountGrowth series, Color ink, float fontSize, decimal latest)
        {
            var plotWidth = dirtyRect.Width - BalanceGutter;
            var plotBottom = dirtyRect.Height - 4;
            canvas.StrokeSize = 1f;
            canvas.StrokeColor = PaperResources.Colour("InkSoft");
            canvas.DrawLine(0, plotBottom, plotWidth, plotBottom);

            var lineHeight = fontSize * 1.4f;
            canvas.FontSize = fontSize;
            canvas.Font = Face("Mono", FontWeights.Regular);
            canvas.FontColor = ink;

            var maxValue = (double)series.Points.Max(p => p.Balance);
            if (series.Points.Count < 2 || maxValue <= 0)
            {
                canvas.DrawString(PlanMoney.Whole(latest), plotWidth + 4, 0, BalanceGutter - 4, lineHeight,
                    HorizontalAlignment.Left, VerticalAlignment.Top);
                return;
            }

            var firstMonth = series.Points[0].MonthOf;
            var lastMonth = series.Points[^1].MonthOf;
            var top = lineHeight / 2f;

            float YFor(decimal balance) => plotBottom - (float)((double)balance / maxValue) * (plotBottom - top);

            var path = new PathF();
            for (var i = 0; i < series.Points.Count; i++)
            {
                var x = (float)GrowthChartScale.FractionAcross(series.Points[i].MonthOf, firstMonth, lastMonth) * plotWidth;
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
            canvas.DrawPath(path);

            // The latest balance sits at the line's end, in the gutter (mockup 9).
            canvas.DrawString(PlanMoney.Whole(latest), plotWidth + 4, YFor(latest) - lineHeight / 2f, BalanceGutter - 4, lineHeight,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        // Once a year from the first recorded month (mockup 9: "Sep '25 / Sep '26 / Sep '27"), every few years
        // when the horizon is long enough that a label a year would overlap. The first label is set from the
        // plot's left edge; the others are centred on their month and kept inside the plot.
        private static void DrawYearLabels(
            ICanvas canvas, float plotWidth, float labelY, Color softInk, float fontSize, DateOnly first, DateOnly end)
        {
            const float LabelWidth = 64f;
            // Neighbouring labels sit at least a label and a half apart, so a clamped last label (pulled in to
            // stay inside the plot) still clears the one before it.
            const float LabelSpacing = LabelWidth * 1.5f;
            canvas.FontSize = fontSize;
            canvas.FontColor = softInk;
            canvas.Font = Face("Sans", FontWeights.Regular);

            var ticks = GrowthChartAxis.YearTicks(first, end, plotWidth, LabelSpacing);
            for (var i = 0; i < ticks.Count; i++)
            {
                var x = (float)GrowthChartScale.FractionAcross(ticks[i].Month, first, end) * plotWidth;
                if (i == 0)
                {
                    canvas.DrawString(ticks[i].Label, 0, labelY, LabelWidth, fontSize * 1.4f,
                        HorizontalAlignment.Left, VerticalAlignment.Top);
                    continue;
                }

                var left = Math.Clamp(x - LabelWidth / 2f, 0f, Math.Max(0f, plotWidth - LabelWidth));
                canvas.DrawString(ticks[i].Label, left, labelY, LabelWidth, fontSize * 1.4f,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }
    }
}
