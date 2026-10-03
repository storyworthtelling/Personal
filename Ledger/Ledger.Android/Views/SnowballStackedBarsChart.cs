using Index.Android.Pages;
using Index.Domain.Model;
using Microsoft.Maui.Graphics;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>One recorded week, every loan's balance in the same FOCUS/payoff order the Loans side
/// shows (index 0 = first to pay off; the last index = the mortgage).</summary>
public sealed record StackedBarWeek(DateOnly WeekOf, IReadOnlyList<decimal> BalancesInPayoffOrder);

/// <summary>
/// D136/D138: the sheet's "Loans Chart" tab, redrawn — one bar per recorded week, one stacked segment
/// per loan, in a green ramp running darkest (soonest paid off) to lightest (the mortgage, D138: "in
/// its own panel... at the foot of the stacked bars"). Segments stack bottom-to-top in REVERSE payoff
/// order, so the mortgage (lightest, largest) forms the base and the soonest-to-clear loans sit as
/// thin dark slivers on top — "the segment disappearing next sits on top" (D113's own wording). Thin
/// lines only (2px), no filled role colour, per the design book's stated chart convention.
///
/// <para>Mockup 6 also draws a money axis (round gridlines labelled in the gutter to the right of the
/// plot — <see cref="ChartAxis.NiceSteps"/>) and calendar-month labels under it
/// (<see cref="ChartAxis.MonthTicks"/>). Touch is resolved by <see cref="ChartHit.AtBar"/> against the
/// PLOT's width, not the view's, so a touch in the gutter is no week.</para>
/// </summary>
public sealed class SnowballStackedBarsChart : GraphicsView
{
    // Beside the plot, for the gridlines' labels ("300k"); below it, for the month labels.
    private const float AxisGutter = 44f;
    private const float MonthLabelBand = 20f;

    private readonly Painter painter = new();

    public SnowballStackedBarsChart()
    {
        Drawable = painter;
        HeightRequest = 160;
        SizeChanged += (_, _) => Invalidate();

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);
    }

    /// <summary>Touching a bar (mockup 6: "Touching a bar or a panel shows that week's date and
    /// balance") — a plain event the page turns into an inline label swap; no tooltip UI.</summary>
    public event Action<DateOnly, decimal>? WeekTapped;

    public void SetWeeks(IReadOnlyList<StackedBarWeek> weeks)
    {
        painter.Weeks = weeks;
        SemanticProperties.SetDescription(this, weeks.Count == 0
            ? null
            : $"Stacked weekly bars of every loan, {SnowballHistoryCopy.WeekCaption(weeks[0].WeekOf)} to "
              + $"{SnowballHistoryCopy.WeekCaption(weeks[^1].WeekOf)}: {MainPage.Money(weeks[0].BalancesInPayoffOrder.Sum(), "USD")} then, "
              + $"{MainPage.Money(weeks[^1].BalancesInPayoffOrder.Sum(), "USD")} now.");
        Invalidate();
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (painter.Weeks.Count == 0 || e.GetPosition(this) is not { } point)
        {
            return; // no position means no week: never guess "the latest" (R-G24)
        }

        if (ChartHit.AtBar((float)point.X, (float)Width - AxisGutter, painter.Weeks.Count) is not { } index)
        {
            return; // the axis gutter beside the plot
        }

        var week = painter.Weeks[index];
        WeekTapped?.Invoke(week.WeekOf, week.BalancesInPayoffOrder.Sum());
    }

    private sealed class Painter : IDrawable
    {
        public IReadOnlyList<StackedBarWeek> Weeks { get; set; } = [];

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Weeks.Count == 0)
            {
                return;
            }

            var maxTotal = Weeks.Max(w => w.BalancesInPayoffOrder.Sum());
            if (maxTotal <= 0m)
            {
                return;
            }

            var chartHeight = dirtyRect.Height - MonthLabelBand;
            var plotWidth = dirtyRect.Width - AxisGutter;
            var barWidth = plotWidth / Weeks.Count;
            var loanCount = Weeks[0].BalancesInPayoffOrder.Count;

            var fontSize = (float)PaperResources.Token<double>("LabelSize");
            canvas.FontSize = fontSize;
            DrawGridlines(canvas, fontSize, maxTotal, plotWidth, chartHeight);

            for (var w = 0; w < Weeks.Count; w++)
            {
                var x = w * barWidth;
                var balances = Weeks[w].BalancesInPayoffOrder;
                var top = chartHeight; // the running top of the stack, built bottom-up

                // Bottom-to-top: the LAST loan in payoff order (the mortgage) is drawn first, forming
                // the base of the stack; the FIRST loan (soonest to clear) is drawn last, on top.
                for (var i = loanCount - 1; i >= 0; i--)
                {
                    var value = i < balances.Count ? balances[i] : 0m;
                    if (value <= 0m)
                    {
                        continue;
                    }

                    var segmentHeight = (float)((double)value / (double)maxTotal) * chartHeight;
                    canvas.FillColor = Color.FromArgb(SnowballChartPalette.RampHex(i, loanCount));
                    canvas.FillRectangle(x, top - segmentHeight, Math.Max(1f, barWidth - 1f), segmentHeight);
                    top -= segmentHeight;
                }
            }

            canvas.StrokeSize = 1f;
            canvas.StrokeColor = PaperResources.Colour("InkSoft");
            canvas.DrawLine(0, chartHeight, plotWidth, chartHeight);

            DrawMonthLabels(canvas, fontSize, barWidth, dirtyRect.Width, chartHeight + 2);
        }

        // Mockup 6: dashed hairlines behind the bars at round money steps, each labelled in the gutter
        // to the right of the plot. The bars normalise to the largest total, so a step's height is the
        // same ratio the bars use.
        private static void DrawGridlines(ICanvas canvas, float fontSize, decimal maxTotal, float plotWidth, float chartHeight)
        {
            var labelHeight = fontSize * 1.4f;
            foreach (var step in ChartAxis.NiceSteps(maxTotal))
            {
                var y = chartHeight - (float)((double)step / (double)maxTotal) * chartHeight;

                canvas.StrokeSize = 1f;
                canvas.StrokeColor = PaperResources.Colour("InkMuted").WithAlpha(0.35f);
                canvas.StrokeDashPattern = [2f, 3f];
                canvas.DrawLine(0, y, plotWidth, y);
                canvas.StrokeDashPattern = null;

                canvas.FontColor = PaperResources.Colour("InkSoft");
                canvas.DrawString(
                    ChartAxis.MoneyTick(step), plotWidth + 4, Math.Max(0f, y - labelHeight / 2f), AxisGutter - 4, labelHeight,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }

        // Calendar months (Jan and Jul), each under the week that contains the first — not 0/50/100% of
        // the weeks. The label is centred on its bar and nudged back inside the view at the edges.
        private void DrawMonthLabels(ICanvas canvas, float fontSize, float barWidth, float viewWidth, float labelY)
        {
            canvas.FontColor = PaperResources.Colour("InkSoft");
            const float LabelWidth = 64f;
            var labelHeight = fontSize * 1.4f;

            foreach (var tick in ChartAxis.MonthTicks(Weeks.Select(w => w.WeekOf).ToList()))
            {
                var centre = (tick.WeekIndex + 0.5f) * barWidth;
                var left = Math.Clamp(centre - LabelWidth / 2f, 0f, Math.Max(0f, viewWidth - LabelWidth));
                canvas.DrawString(
                    tick.Label, left, labelY, LabelWidth, labelHeight,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }
    }
}
