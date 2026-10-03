using System.Globalization;
using Index.Domain.Model;
using Microsoft.Maui.Graphics;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>One loan's whole recorded balance series, plus its own peak — the small-multiple's own
/// scale runs $0 to THIS value, never a scale shared across loans (mockup 6's own foot note).</summary>
public sealed record SmallMultipleSeries(string Name, IReadOnlyList<decimal> Balances, decimal Peak, bool IsPaidOff);

/// <summary>
/// D136/D138: the sheet's "Loans Except Mortgage" tab, redrawn as small multiples — one panel per
/// loan (mortgage included, D138), each on its OWN $0-to-its-own-peak scale, because "eight lines in
/// one 300dp chart would need eight colours, and the design system has no palette for that" (the
/// task's own framing of D138). Every panel draws in the one fixed ink-blue
/// (<see cref="SnowballChartPalette.InkBlueHex"/>), thin (2px), no role colour. A paid-off loan's
/// panel still draws, ending flat at zero.
///
/// <para>Mockup 6's grid is two columns reading across; an odd last panel (the ninth, the mortgage)
/// keeps its column at the same half width — <see cref="SmallMultipleLayout"/> decides every panel's box,
/// for painting and for touch alike. A touch resolves to a panel and then, by
/// <see cref="ChartHit.AtPoint"/> over that panel's own width, to a week (R-G24).</para>
/// </summary>
public sealed class SnowballSmallMultiplesChart : GraphicsView
{
    private const float PanelHeight = 88f;
    private const float PanelGap = 14f;

    private readonly Painter painter = new();

    public SnowballSmallMultiplesChart()
    {
        Drawable = painter;
        SizeChanged += (_, _) => Invalidate();

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);
    }

    /// <summary>Touching a panel (mockup 6: "Touching a bar or a panel shows that week's date and
    /// balance").</summary>
    public event Action<string, DateOnly, decimal>? PointTapped;

    public void SetSeries(IReadOnlyList<DateOnly> weeks, IReadOnlyList<SmallMultipleSeries> series)
    {
        painter.Weeks = weeks;
        painter.Series = series;
        HeightRequest = SmallMultipleLayout.TotalHeight(series.Count, PanelHeight, PanelGap);
        Invalidate();
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (painter.Series.Count == 0 || painter.Weeks.Count == 0 || e.GetPosition(this) is not { } point)
        {
            return; // no position means no week: never guess "the latest" (R-G24)
        }

        if (SmallMultipleLayout.HitTest(
                (float)point.X, (float)point.Y, painter.Series.Count, (float)Width, PanelHeight, PanelGap) is not { } panel)
        {
            return; // the gutter between panels, or the empty half of the last row
        }

        var series = painter.Series[panel.Index];
        var weekCount = Math.Min(series.Balances.Count, painter.Weeks.Count);
        if (ChartHit.AtPoint(panel.LocalX, panel.PanelWidth, weekCount) is not { } week)
        {
            return;
        }

        PointTapped?.Invoke(series.Name, painter.Weeks[week], series.Balances[week]);
    }

    private sealed class Painter : IDrawable
    {
        public IReadOnlyList<DateOnly> Weeks { get; set; } = [];
        public IReadOnlyList<SmallMultipleSeries> Series { get; set; } = [];

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Series.Count == 0)
            {
                return;
            }

            var fontSize = (float)PaperResources.Token<double>("LabelSize");
            canvas.FontSize = fontSize;

            // A header that cannot fit name and figure on one line wraps the name onto a second. A whole
            // row wraps together, so the panels beside each other keep one baseline.
            var panelWidth = SmallMultipleLayout.PanelWidth(dirtyRect.Width, PanelGap);
            var wraps = Series.Select(series => NeedsTwoLines(canvas, series, fontSize, panelWidth)).ToList();

            for (var i = 0; i < Series.Count; i++)
            {
                var rowStart = i / SmallMultipleLayout.Columns * SmallMultipleLayout.Columns;
                var twoLines = Enumerable.Range(rowStart, Math.Min(SmallMultipleLayout.Columns, Series.Count - rowStart))
                    .Any(j => wraps[j]);

                DrawPanel(canvas, Series[i], SmallMultipleLayout.Panel(i, dirtyRect.Width, PanelHeight, PanelGap), fontSize, twoLines);
            }
        }

        private static string ValueText(SmallMultipleSeries series) =>
            series.IsPaidOff ? "Paid off" : Money(series.Balances.Count > 0 ? series.Balances[^1] : 0m);

        private static bool NeedsTwoLines(ICanvas canvas, SmallMultipleSeries series, float fontSize, float panelWidth)
        {
            var name = canvas.GetStringSize(series.Name, Microsoft.Maui.Graphics.Font.Default, fontSize).Width;
            var value = canvas.GetStringSize(ValueText(series), Microsoft.Maui.Graphics.Font.Default, fontSize).Width;
            return name + value + HeaderGap > panelWidth;
        }

        private const float HeaderGap = 8f;

        private static void DrawPanel(ICanvas canvas, SmallMultipleSeries series, PanelRect panel, float fontSize, bool twoLines)
        {
            var lineHeight = fontSize * 1.4f;
            var headerHeight = lineHeight * (twoLines ? 2 : 1);
            var value = ValueText(series);
            var valueWidth = canvas.GetStringSize(value, Microsoft.Maui.Graphics.Font.Default, fontSize).Width;

            canvas.FontSize = fontSize;
            canvas.FontColor = PaperResources.Colour("Ink");
            canvas.DrawString(
                series.Name, panel.X, panel.Y, panel.Width - valueWidth - HeaderGap, headerHeight,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            canvas.DrawString(
                value, panel.X, panel.Y, panel.Width, lineHeight, HorizontalAlignment.Right, VerticalAlignment.Top);

            var plotTop = panel.Y + headerHeight + 2f;
            var plotBottom = panel.Y + panel.Height - lineHeight - 2f;
            var plotHeight = plotBottom - plotTop;

            canvas.StrokeSize = 1f;
            canvas.StrokeColor = PaperResources.Colour("Hairline");
            canvas.DrawLine(panel.X, plotBottom, panel.X + panel.Width, plotBottom);

            if (series.Peak > 0m && series.Balances.Count > 1)
            {
                canvas.StrokeSize = 2f;
                canvas.StrokeColor = Color.FromArgb(SnowballChartPalette.InkBlueHex());

                var path = new PathF();
                for (var p = 0; p < series.Balances.Count; p++)
                {
                    var px = panel.X + (float)p / (series.Balances.Count - 1) * panel.Width;
                    var ratio = (double)series.Balances[p] / (double)series.Peak;
                    var py = plotBottom - (float)ratio * plotHeight;
                    if (p == 0)
                    {
                        path.MoveTo(px, py);
                    }
                    else
                    {
                        path.LineTo(px, py);
                    }
                }
                canvas.DrawPath(path);
            }

            canvas.FontColor = PaperResources.Colour("InkSoft");
            canvas.DrawString(
                $"peak {Money(series.Peak)}", panel.X, panel.Y + panel.Height - lineHeight, panel.Width, lineHeight,
                HorizontalAlignment.Left, VerticalAlignment.Top);
        }

        private static string Money(decimal value) =>
            value.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
    }
}
