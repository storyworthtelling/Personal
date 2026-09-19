using Folio.Domain.Analysis;
using Folio.Domain.Readiness;
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

    /// <summary>
    /// A changed scene's before and after, side by side under their own labels (F41).
    ///
    /// <para>Two half-width panels rather than two full-width ones: a reader comparing them looks
    /// from one to the other, and a page turn between them is not a comparison. They are labelled in
    /// words, because which of two pictures is the older one is not something a picture can say.</para>
    /// </summary>
    public void ScenePair(byte[] previous, byte[] current)
    {
        var height = PageGeometry.SceneImageHeight * 0.62f;
        var gutter = PageGeometry.BlockGap;
        var width = (ContentWidth - gutter) / 2f;
        var font = ReportType.Small();

        if (y + height + font.Spacing > ContentBottom && y > ContentTop)
            NewPage();

        Panel(previous, ContentLeft, width, height, "Previous draft", font);
        Panel(current, ContentLeft + width + gutter, width, height, "This draft", font);

        y += height + font.Spacing + PageGeometry.TightGap;
    }

    private void Panel(byte[] png, float left, float width, float height, string label, SKFont font)
    {
        var dest = new SKRect(left, y, left + width, y + height);
        if (!SkiaImageFit.Draw(canvas, png, dest))
        {
            using var shade = new SKPaint { Color = ReportPalette.Hairline, IsAntialias = false };
            canvas.DrawRect(dest, shade);
        }

        using var ink = new SKPaint { Color = ReportPalette.InkMuted, IsAntialias = true };
        canvas.DrawText(label, left, y + height + font.Size + 1f, SKTextAlign.Left, font, ink);
    }

    /// <summary>
    /// Tension across the chapters, with the revision plan drawn on it: a finding over a range
    /// recolours that run of the line in its severity, a single-chapter finding is a dot, and a sag
    /// takes the hue of whatever sits on it (F39).
    ///
    /// <para>It is a line and not filled bands, and the reason is measured in
    /// <see cref="PacingChart"/>: the Document profile's four severity tones are equiluminant, so
    /// the edge between two adjacent fills would carry about 1.02:1 and exist in hue alone. Each run
    /// therefore also carries its <see cref="SeverityCue"/> weight and dash, which is §16's
    /// non-colour cue and what makes the chart survive the grayscale panel it is read on.</para>
    /// </summary>
    public void PacingChart(IReadOnlyList<ChapterTension> curve, IReadOnlyList<PacingFinding> findings)
    {
        if (curve is not { Count: > 0 })
            return;

        var height = PageGeometry.PacingChartHeight;
        var width = ContentWidth;
        if (y + height > ContentBottom && y > ContentTop)
            NewPage();

        var left = ContentLeft;
        var bottom = y + height;
        var overlay = Domain.Report.PacingChart.Overlay(curve, findings ?? []);

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

        // A sag is a bracket *under* the rule, not a wash behind the line. Rendered as a pale fill it
        // came out as a smear sitting on the baseline and read as a printing fault; a bracket is a
        // drawn mark, survives the grayscale panel, and cannot be mistaken for the series.
        foreach (var sag in overlay.Sags)
        {
            var from = points[Math.Clamp(sag.FromChapter, 0, points.Length - 1)].X;
            var to = points[Math.Clamp(sag.ToChapter, 0, points.Length - 1)].X;
            var rule = bottom + SagBandDepth;
            using var bracket = new SKPaint
            {
                Color = Hue(sag.Severity), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f,
            };
            canvas.DrawLine(from, rule, to, rule, bracket);
            canvas.DrawLine(from, rule, from, rule - 3f, bracket);
            canvas.DrawLine(to, rule, to, rule - 3f, bracket);
        }

        foreach (var run in overlay.Runs)
        {
            var cue = run.Severity is { } severity ? SeverityCue.Stroke(severity) : SeverityCue.Plain;
            using var line = new SKPaint
            {
                Color = Hue(run.Severity), IsAntialias = true, StrokeWidth = (float)cue.Width,
                Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round,
                PathEffect = cue.IsSolid
                    ? null
                    : SKPathEffect.CreateDash([.. cue.Dash.Select(length => (float)length)], 0),
            };

            for (var i = run.FromChapter + 1; i <= run.ToChapter && i < points.Length; i++)
                canvas.DrawLine(points[i - 1], points[i], line);
        }

        using (var dot = new SKPaint { Color = ReportPalette.InkMuted, IsAntialias = true, Style = SKPaintStyle.Fill })
            foreach (var point in points)
                canvas.DrawCircle(point.X, point.Y, 1.5f, dot);

        // A single-chapter finding: a ringed dot, so it is a different shape from the chapter marks
        // rather than the same shape in a colour.
        foreach (var marked in overlay.Dots)
        {
            var point = points[Math.Clamp(marked.Chapter, 0, points.Length - 1)];
            using var ring = new SKPaint
            {
                Color = Hue(marked.Severity), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f,
            };
            canvas.DrawCircle(point.X, point.Y, 4f, ring);
        }

        y = bottom + SagBandDepth;
        Key(overlay);
    }

    /// <summary>
    /// Names the severities this chart drew, in their own stroke and hue.
    ///
    /// <para>Without it the claim the chart rests on is not true: a reader would be inferring
    /// severity from colour, which is the thing §16 forbids and the thing the runs' weights and
    /// dashes exist to avoid. It lists only what was drawn, so a clean chart carries no key.</para>
    /// </summary>
    private void Key(PacingOverlay overlay)
    {
        var drawn = overlay.Runs.Select(run => run.Severity)
            .Concat(overlay.Dots.Select(dot => (ReadinessTier?)dot.Severity))
            .Where(severity => severity is not null)
            .Select(severity => severity!.Value)
            .Distinct()
            .OrderBy(severity => severity)
            .ToList();

        if (drawn.Count == 0)
            return;

        var font = ReportType.Small();

        // A whole line clear of the sag brackets hanging under the rule. Set to one gap, the key's
        // swatches printed alongside a bracket and the two read as one row of marks.
        y += font.Spacing;

        var x = ContentLeft;
        foreach (var severity in drawn)
        {
            var cue = SeverityCue.Stroke(severity);
            using (var sample = new SKPaint
            {
                Color = Hue(severity), IsAntialias = true, StrokeWidth = (float)cue.Width,
                Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round,
                PathEffect = cue.IsSolid
                    ? null
                    : SKPathEffect.CreateDash([.. cue.Dash.Select(length => (float)length)], 0),
            })
                canvas.DrawLine(x, y, x + 18f, y, sample);

            var label = severity.ToString();
            using (var ink = new SKPaint { Color = ReportPalette.InkMuted, IsAntialias = true })
                canvas.DrawText(label, x + 23f, y + 3f, SKTextAlign.Left, font, ink);

            x += 23f + font.MeasureText(label) + 20f;
        }

        y += PageGeometry.BlockGap;
    }

    /// <summary>How far below the chart's rule a sag bracket hangs.</summary>
    private const float SagBandDepth = 5f;

    /// <summary>The severity's printed hue; the plain line where nothing is wrong.</summary>
    private static SKColor Hue(ReadinessTier? severity) => severity switch
    {
        ReadinessTier.Must => ReportPalette.MustFix,
        ReadinessTier.Should => ReportPalette.Should,
        ReadinessTier.Consider => ReportPalette.Consider,
        ReadinessTier.Great => ReportPalette.Great,
        _ => ReportPalette.Rule,
    };

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
