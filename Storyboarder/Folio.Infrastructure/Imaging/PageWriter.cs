using Folio.Domain.Analysis;
using Folio.Domain.Report;
using SkiaSharp;
using Storyworthtelling.Paper;

namespace Folio.Infrastructure.Imaging;

/// <summary>
/// The report's page: one flowing column of type, broken where it runs out of sheet, written
/// straight into a <see cref="VectorPdfSink"/>.
///
/// <para>It used to snapshot each page to a 1240 × 1754 PNG and hand the stack to PdfSharp — a
/// picture of a page, in which nothing could be selected or searched, and which an e-ink panel
/// resamples twice. The draw calls below are the same ones; only where they land is different,
/// because a PDF's user space is already the unit the page is measured in.</para>
///
/// <para><b>The folio counts every sheet, and the cover prints none.</b> The report used to leave
/// front matter outside the numbering and start the content at 1, which meant the contents listed a
/// number that was not the number on the page — a reader at folio 61 had to know how many sheets came
/// before it. The drawn pages number straight through (the frame carries "2"), so a page's folio and
/// its place in the file are the same thing. The title page is the ordinary exception: it is page one
/// and says so by being first.</para>
/// </summary>
internal sealed class PageWriter : IDisposable
{
    private const float ContentLeft = PageGeometry.ContentLeft;
    private const float ContentWidth = PageGeometry.ContentWidth;
    private const float ContentTop = PageGeometry.ContentTop;
    private const float ContentBottom = PageGeometry.ContentBottom;

    /// <summary>The grain tile was sized for a 150-DPI page; this keeps it the same size on paper.</summary>
    private const float GrainScale = 72f / 150f;

    private readonly string runningFoot;
    private readonly VectorPdfSink sink = new();
    private readonly List<(string Title, int Page)> tocEntries = [];

    private SKCanvas canvas = null!;
    private bool folioSuppressed;
    private int finishedPages;
    private float y;

    public PageWriter(string runningFoot)
    {
        this.runningFoot = runningFoot;
        StartPage();
    }

    /// <summary>H2 sections and the folio they begin on.</summary>
    public IReadOnlyList<(string Title, int Page)> TocEntries => tocEntries;

    /// <summary>
    /// The folio the next page will carry. Set before the content so its sections record the number a
    /// reader will actually see, rather than a count that starts again after the front matter.
    /// </summary>
    public int FirstFolio { get; init; } = 1;

    /// <summary>Pages written so far, including the one in progress.</summary>
    public int PageCount => sink.PageCount;

    /// <summary>
    /// Keeps this page out of the running foot. The title page is the only one that asks: it is page
    /// one and says so by being first, so printing the number on it would be telling the reader
    /// something the page already says.
    /// </summary>
    public void SuppressFolio() => folioSuppressed = true;

    public SKCanvas Canvas => canvas;

    public float Y
    {
        get => y;
        set => y = value;
    }

    public void Gap(float height) => y += height;

    // Break to a fresh page if the next `height` points wouldn't fit, so a block (a scene's image +
    // citation) isn't split across a page boundary.
    public void KeepTogether(float height)
    {
        if (y + height > ContentBottom && y > ContentTop)
            NewPage();
    }

    public void Heading(string text, SKFont font, SKColor color, bool ruled = false)
    {
        y += PageGeometry.RuleGapAbove;
        if (y + font.Size * PageGeometry.LineSpacing * 2 > ContentBottom)
            NewPage();
        if (Math.Abs(font.Size - PaperTypeScale.Title) < 0.01f)
            tocEntries.Add((text, FirstFolio + finishedPages));
        DrawWrapped(text, font, color, ContentLeft, ContentWidth);
        if (ruled)
        {
            Rule(ContentLeft, y + 2f, ContentWidth / 3);
            y += PageGeometry.RuleGapBelow;
        }
        y += PageGeometry.TightGap;
    }

    public void Paragraph(string text, SKFont font, SKColor color)
    {
        DrawWrapped(text, font, color, ContentLeft, ContentWidth);
        y += PageGeometry.TightGap;
    }

    public void Bullet(string text, SKFont font, SKColor color)
    {
        var lineHeight = font.Size * PageGeometry.LineSpacing;
        var lines = SkiaText.Wrap(font, text, ContentWidth - PageGeometry.BulletIndent);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        using var bulletPaint = new SKPaint { Color = ReportPalette.Rule, IsAntialias = true };
        for (var i = 0; i < lines.Count; i++)
        {
            if (y + lineHeight > ContentBottom)
                NewPage();
            var baseline = y + font.Size;
            if (i == 0)
                canvas.DrawText("•", ContentLeft, baseline, font, bulletPaint);
            canvas.DrawText(lines[i], ContentLeft + PageGeometry.BulletIndent, baseline, font, paint);
            y += lineHeight;
        }
        y += 2f;
    }

    /// <summary>A rule in the Document Action hue — the mark under a heading.</summary>
    public void Rule(float left, float top, float width)
    {
        using var rule = new SKPaint { Color = ReportPalette.Rule, IsAntialias = true };
        canvas.DrawRect(left, top, width, PageGeometry.RuleHeight, rule);
    }

    /// <summary>The hairline that separates blocks within a page.</summary>
    public void Hairline()
    {
        y += PageGeometry.HairlineGap;
        using var paint = new SKPaint { Color = ReportPalette.Hairline, IsAntialias = true };
        canvas.DrawRect(ContentLeft, y, ContentWidth, PageGeometry.HairlineHeight, paint);
        y += PageGeometry.HairlineGap;
    }

    /// <summary>
    /// A field label: caps, letterspaced, in the muted ink — the apparatus voice, DESIGN.md §4.
    /// Skia has no tracking, so the advance is walked by hand.
    /// </summary>
    public void Label(string text, float x, float baseline, SKColor? color = null)
    {
        using var font = ReportType.Label();
        using var paint = new SKPaint { Color = color ?? ReportPalette.InkMuted, IsAntialias = true };
        var tracking = font.Size * 0.1f;
        var cursor = x;
        foreach (var glyph in text.ToUpperInvariant())
        {
            var s = glyph.ToString();
            canvas.DrawText(s, cursor, baseline, font, paint);
            cursor += font.MeasureText(s) + tracking;
        }
    }

    public float LabelWidth(string text)
    {
        using var font = ReportType.Label();
        var tracking = font.Size * 0.1f;
        return text.ToUpperInvariant().Sum(glyph => font.MeasureText(glyph.ToString()) + tracking);
    }

    // Draws one wordless evidence image: a portrait block, letterboxed on shaded paper, breaking to a
    // fresh page if it wouldn't fit.
    public void Image(byte[] png)
    {
        var height = PageGeometry.SceneImageHeight;
        var width = PageGeometry.SceneImageWidth;
        if (y + height > ContentBottom && y > ContentTop)
            NewPage();

        var dest = new SKRect(ContentLeft, y, ContentLeft + width, y + height);
        if (!SkiaImageFit.Draw(canvas, png, dest))
        {
            // Undecodable bytes: leave a shaded slot rather than crashing the whole report.
            using var shade = new SKPaint { Color = ReportPalette.Hairline, IsAntialias = false };
            canvas.DrawRect(dest, shade);
        }
        y += height + PageGeometry.TightGap;
    }

    // A compact tension-over-chapters chart: an area under a polyline, so a saggy stretch reads at a
    // glance. Values are clamped 0..1; an empty curve draws nothing.
    public void PacingChart(IReadOnlyList<ChapterTension> curve)
    {
        if (curve is not { Count: > 0 })
            return;

        var height = PageGeometry.PacingChartHeight;
        var width = ContentWidth;
        if (y + height > ContentBottom && y > ContentTop)
            NewPage();

        var left = ContentLeft;
        var bottom = y + height;

        using (var frame = new SKPaint
        {
            Color = ReportPalette.InkMuted, IsAntialias = true, StrokeWidth = 1, Style = SKPaintStyle.Stroke,
        })
            canvas.DrawLine(left, bottom, left + width, bottom, frame);

        var points = new SKPoint[curve.Count];
        var step = curve.Count == 1 ? 0 : width / (curve.Count - 1);
        for (var i = 0; i < curve.Count; i++)
        {
            var px = curve.Count == 1 ? left + width / 2 : left + i * step;
            var t = (float)Math.Clamp(curve[i].Tension, 0, 1);
            var py = bottom - t * (height - 3f);
            points[i] = new SKPoint(px, py);
        }

        using (var area = new SKPath())
        {
            area.MoveTo(points[0].X, bottom);
            foreach (var point in points)
                area.LineTo(point.X, point.Y);
            area.LineTo(points[^1].X, bottom);
            area.Close();
            using var fill = new SKPaint
            {
                Color = ReportPalette.Rule.WithAlpha(0x33), IsAntialias = true, Style = SKPaintStyle.Fill,
            };
            canvas.DrawPath(area, fill);
        }

        using (var line = new SKPaint
        {
            Color = ReportPalette.Rule, IsAntialias = true, StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round,
        })
            for (var i = 1; i < points.Length; i++)
                canvas.DrawLine(points[i - 1], points[i], line);

        using (var dot = new SKPaint { Color = ReportPalette.Rule, IsAntialias = true, Style = SKPaintStyle.Fill })
            foreach (var point in points)
                canvas.DrawCircle(point.X, point.Y, 1.5f, dot);

        y = bottom + PageGeometry.BlockGap;
    }

    /// <summary>Closes the last page and returns the document's bytes.</summary>
    public byte[] Finish()
    {
        FinishPage();
        return sink.Close();
    }

    public void Dispose() => sink.Dispose();

    public void NewPage()
    {
        FinishPage();
        StartPage();
    }

    private void DrawWrapped(string text, SKFont font, SKColor color, float x, float wrapWidth)
    {
        var lineHeight = font.Size * PageGeometry.LineSpacing;
        var lines = SkiaText.Wrap(font, text, wrapWidth);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        foreach (var line in lines)
        {
            if (y + lineHeight > ContentBottom)
                NewPage();
            canvas.DrawText(line, x, y + font.Size, font, paint);
            y += lineHeight;
        }
    }

    private void StartPage()
    {
        canvas = sink.BeginPage(PageGeometry.PageWidth, PageGeometry.PageHeight);
        canvas.Clear(ReportPalette.Ground);
        PaperGrain.Draw(canvas, PageGeometry.PageWidth, PageGeometry.PageHeight, GrainScale);
        y = ContentTop;
    }

    private void FinishPage()
    {
        if (!folioSuppressed)
        {
            DrawFooter();
        }
        folioSuppressed = false;
        finishedPages++;
        sink.EndPage();
    }

    private void DrawFooter()
    {
        using var running = ReportType.Running();
        using var folio = ReportType.Folio();
        var baseline = PageGeometry.PageHeight - PageGeometry.FolioBaseline;

        using var titlePaint = new SKPaint { Color = ReportPalette.InkMuted, IsAntialias = true };
        canvas.DrawText(runningFoot, ContentLeft, baseline, running, titlePaint);

        var number = (FirstFolio + finishedPages).ToString();
        using var numberPaint = new SKPaint { Color = ReportPalette.InkMuted, IsAntialias = true };
        canvas.DrawText(number, PageGeometry.ContentRight - folio.MeasureText(number),
            baseline, folio, numberPaint);
    }
}
