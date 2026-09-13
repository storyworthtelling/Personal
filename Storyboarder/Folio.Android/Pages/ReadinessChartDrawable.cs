using Folio.Domain.Readiness;
using Microsoft.Maui.Graphics;
using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>
/// Readiness across a book's drafts: four lines, each named at its own end.
///
/// <para><b>Lines, never fills.</b> Run as four adjacent stacked areas the severity tones fail the
/// adjacent-fill contrast floor — Must against Should measures ΔE 12.6 in normal vision, under the
/// floor of 15, and reordering made it worse. A line is not an adjacent fill: it is separated by
/// path and by position, carries its own marker, and says its name in words at its end. Colour is
/// the last thing identity rests on here, not the first.</para>
///
/// <para>All the arithmetic — the points, and the pushing-apart of labels that would otherwise
/// print on top of each other — is <see cref="ReadinessChart"/>, tested away from the device.</para>
///
/// <para>Colour comes from <c>PaperColorTokens</c> rather than from the merged dictionary, as the
/// shelf's does. A drawable that reads <c>Application.Current.Resources</c> needs a running app to
/// paint at all, which makes it unrenderable outside one — and rendering these off-device, beside
/// the frame they were drawn from, is how three gaps in the desk were found.</para>
/// </summary>
public sealed class ReadinessChartDrawable : IDrawable
{
    /// <summary>The frame's own canvas, which the pure geometry is expressed in.</summary>
    public const float FrameWidth = 287f;
    public const float FrameHeight = 122f;

    private const float MarkerRadius = 4.5f;
    private const float LabelSize = 11f;

    public required ReadinessPlot Plot { get; init; }

    public required IReadOnlyDictionary<ReadinessTier, string> Labels { get; init; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // The geometry is written in the frame's coordinates; scale once rather than everywhere.
        canvas.SaveState();
        canvas.Scale(dirtyRect.Width / FrameWidth, dirtyRect.Height / FrameHeight);

        canvas.StrokeSize = 1;
        canvas.StrokeColor = Color.FromArgb(PaperColorTokens.Hairline);
        canvas.DrawLine(6, (float)ReadinessChart.BaselineY + 0.5f, 281, (float)ReadinessChart.BaselineY + 0.5f);

        foreach (var series in Plot.Series)
        {
            DrawSeries(canvas, series);
        }

        canvas.RestoreState();
    }

    private void DrawSeries(ICanvas canvas, ReadinessSeries series)
    {
        var ink = InkFor(series.Tier);
        var paper = Color.FromArgb(PaperColorTokens.Paper);

        canvas.StrokeColor = ink;
        canvas.StrokeSize = 2;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        if (series.Points.Count > 1)
        {
            var path = new PathF();
            path.MoveTo((float)series.Points[0].X, (float)series.Points[0].Y);
            foreach (var point in series.Points.Skip(1))
            {
                path.LineTo((float)point.X, (float)point.Y);
            }

            canvas.DrawPath(path);
        }

        // A marker at every draft, ringed in paper so a crossing never looks like a join.
        foreach (var point in series.Points)
        {
            canvas.FillColor = ink;
            canvas.FillCircle((float)point.X, (float)point.Y, MarkerRadius);
            canvas.StrokeColor = paper;
            canvas.StrokeSize = 2;
            canvas.DrawCircle((float)point.X, (float)point.Y, MarkerRadius);
        }

        DrawEndLabel(canvas, series, ink);
    }

    /// <summary>
    /// The name at the end of the line, with a leader bending out to it when the label had to be
    /// pushed off its own line to clear its neighbour.
    /// </summary>
    private void DrawEndLabel(ICanvas canvas, ReadinessSeries series, Color ink)
    {
        var end = series.Points[^1];
        var labelY = (float)series.LabelY;

        canvas.StrokeColor = ink.WithAlpha(0.55f);
        canvas.StrokeSize = 1;
        var leader = new PathF();
        leader.MoveTo((float)end.X + 7, (float)end.Y);
        leader.LineTo((float)end.X + 13, labelY);
        leader.LineTo((float)end.X + 17, labelY);
        canvas.DrawPath(leader);

        canvas.FontColor = Color.FromArgb(PaperColorTokens.InkSoft);
        canvas.FontSize = LabelSize;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;

        // Positioned by baseline rather than inside a box, which is how the frame's own text is
        // placed — and the reason matters: DrawString with a bounding box draws NOTHING AT ALL when
        // the text does not fit it, silently. These labels were invisible for exactly that reason,
        // in a box 12 high that 11pt type overflows once its ascender and descender are counted.
        canvas.DrawString(Labels[series.Tier], (float)end.X + 20, labelY + 3.5f, HorizontalAlignment.Left);
    }

    /// <summary>
    /// Each tier in its role's ink: a Must is a problem, a Should a caution, a Consider is
    /// reference, and a Great is positive. Role tokens, so the chart cannot drift from the report
    /// that produced the counts.
    /// </summary>
    public static Color InkFor(ReadinessTier tier) => tier switch
    {
        ReadinessTier.Must => Color.FromArgb(PaperColorTokens.ProblemMark),
        ReadinessTier.Should => Color.FromArgb(PaperColorTokens.CautionMark),
        ReadinessTier.Consider => Color.FromArgb(PaperColorTokens.ReferenceMark),
        ReadinessTier.Great => Color.FromArgb(PaperColorTokens.PositiveMark),
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "No ink for this tier."),
    };
}
