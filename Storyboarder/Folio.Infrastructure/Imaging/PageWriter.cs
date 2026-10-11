using Folio.Domain.Analysis;
using Folio.Domain.Markup;
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
    private const float ContentRight = PageGeometry.ContentRight;
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
    /// The folio the page in progress carries — the number a reader sees at its foot.
    ///
    /// <para>Deliberately not <see cref="PageCount"/>: the front matter is composed separately and
    /// the body's numbering starts after it, so a section pointing at "page 137" has to mean the
    /// number printed there rather than the count of sheets so far.</para>
    /// </summary>
    public int Folio => FirstFolio + finishedPages;

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

    /// <summary>How far down the sheet the flow has got, and how much of it is left.</summary>
    public float Remaining => ContentBottom - y;

    public bool OnFreshPage => y <= ContentTop + 0.5f;

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
        // A single point has no line to draw between it and anything else — a "trend across
        // chapters" claim needs at least two chapters to trend between. Guarded here too, not just
        // by the caller: a chart that only fails to draw when its caller remembers to check is a
        // chart that will draw the orphaned dot again the next time somebody adds a call site.
        if (curve is not { Count: >= 2 })
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

    /// <summary>
    /// Draws one page of the marked-up manuscript and returns the first line it did <b>not</b>
    /// print, so the caller can ask for the next page.
    ///
    /// <para>The page's length is not a constant: <see cref="MarginColumn"/> packs the notes and
    /// says how much of the text fits beside them, and when a note would fall off the foot the page
    /// ends short so the note and its line arrive on the next sheet together (the owner's Release G
    /// decision). Which is why this returns a line number rather than taking one.</para>
    /// </summary>
    public int ManuscriptPage(
        string manuscript, IReadOnlyList<TextSpan> lines, int first, int limit, bool continuePage,
        ILookup<int, Mark> drawnOn, ILookup<int, Mark> anchored, SKFont prose, SKFont noteFont)
    {
        // A run of lines either opens a fresh sheet or carries on beneath what is already on this one
        // — a scene's block, which is small beside a sheet and should not cost the next scene one.
        if (!continuePage)
            NewPage();

        var origin = continuePage ? y : ContentTop;
        var available = continuePage ? ContentBottom - origin : (float)MarkupGeometry.ContentHeight;
        var perPage = continuePage
            ? Math.Max((int)(available / (float)MarkupGeometry.LineHeight), 1)
            : MarkupGeometry.LinesPerPage;

        var lineHeight = (float)MarkupGeometry.LineHeight;
        var textLeft = ContentLeft;
        var marginLeft = ContentLeft + (float)MarkupGeometry.MarginLeft;
        var noteWidth = (float)MarkupGeometry.MarginWidth;
        var last = Math.Min(Math.Min(first + perPage, lines.Count), limit);

        // What each line's notes come to, in the order the notes will be packed.
        var wanted = new List<(int Line, double AnchorY, double Height)>();
        var wrapped = new Dictionary<int, List<string>>();
        for (var i = first; i < last; i++)
        {
            foreach (var mark in anchored[i])
            {
                var text = NoteText(mark);
                var wraps = SkiaText.Wrap(noteFont, text, noteWidth - 6f);
                wrapped[wanted.Count] = wraps;
                wanted.Add((i, (i - first) * lineHeight, (wraps.Count * noteFont.Spacing) + 6f));
            }
        }

        var packed = MarginColumn.Place(wanted, available);
        var printable = Math.Max((int)Math.Ceiling(packed.TextHeight / lineHeight), 1);
        last = Math.Min(first + printable, last);

        using (var ink = new SKPaint { Color = ReportPalette.Ink, IsAntialias = true })
            for (var i = first; i < last; i++)
            {
                var span = lines[i];
                if (span.Length == 0)
                    continue;

                var baseline = origin + ((i - first) * lineHeight) + prose.Size;
                canvas.DrawText(manuscript[span.Start..span.End], textLeft, baseline, prose, ink);

                foreach (var mark in drawnOn[i])
                    DrawMarkOn(manuscript, new LineSpan(i, span.Start, span.End), mark, textLeft, baseline, prose);
            }

        for (var n = 0; n < packed.Notes.Count; n++)
        {
            var note = packed.Notes[n];
            if (note.AnchorLine >= last)
                continue;   // its line went to the next page with it

            DrawNote(
                packed.Notes[n], wanted[n].Line, wrapped[n], anchored[wanted[n].Line].First(),
                marginLeft, noteWidth, noteFont, lineHeight, prose.Size, origin);
        }

        // The page draws in absolute coordinates, so the flowing cursor has to be told where the
        // manuscript actually left off. Without this it stays at the top of the sheet and every
        // section after the manuscript prints straight through it — which is what the first render
        // of this page did, and no test could see.
        y = origin + ((last - first) * lineHeight);
        return last;
    }

    /// <summary>A mark drawn over the part of this line that it actually covers.</summary>
    private void DrawMarkOn(string manuscript, LineSpan line, Mark mark, float left, float baseline, SKFont prose)
    {
        if (MarkPlacement.RunOn(mark, line) is not { } run)
            return;

        var x = left + prose.MeasureText(manuscript[line.StartOffset..run.Start]);
        var width = mark.IsPoint ? 0f : prose.MeasureText(manuscript[run.Start..run.End]);

        MarkedManuscript.DrawMark(
            canvas, mark, x, baseline, width, MarkedManuscript.Hue(mark.Severity),
            tail: run.End >= mark.EndOffset);
    }

    /// <summary>
    /// The note itself: a severity bracket, the reason, and — when the packing pushed it down — a
    /// leader elbowing back to the line it concerns, so a reader never has to guess which line a
    /// note two inches lower is about.
    /// </summary>
    private void DrawNote(
        PlacedNote note, int line, IReadOnlyList<string> wraps, Mark mark,
        float marginLeft, float noteWidth, SKFont font, float lineHeight, float proseSize, float origin)
    {
        var hue = MarkedManuscript.Hue(mark.Severity);
        var top = origin + (float)note.Top;

        using (var bracket = new SKPaint
        {
            Color = hue, IsAntialias = true, Style = SKPaintStyle.Fill,
        })
            canvas.DrawRect(
                marginLeft - (float)MarkupGeometry.BracketInset - (float)MarkupGeometry.BracketWidth,
                top, (float)MarkupGeometry.BracketWidth, (float)note.Height - 6f, bracket);

        using (var ink = new SKPaint { Color = ReportPalette.InkSoft, IsAntialias = true })
        {
            var y = top + font.Size;
            foreach (var wrap in wraps)
            {
                canvas.DrawText(wrap, marginLeft, y, font, ink);
                y += font.Spacing;
            }
        }

        if (!note.NeedsLeader)
            return;

        // Out of the text, down the gutter, and in to the note — an elbow rather than a diagonal,
        // because a diagonal across a column of prose reads as a mark on the prose.
        var anchorY = origin + (float)note.AnchorY + (proseSize / 2f) + 2f;
        var elbow = ContentLeft + (float)MarkupGeometry.TextWidth + 22f;
        using var leader = new SKPaint
        {
            Color = ReportPalette.Hairline, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.6f,
        };
        canvas.DrawLine(ContentLeft + (float)MarkupGeometry.TextWidth + 6f, anchorY, elbow, anchorY, leader);
        canvas.DrawLine(elbow, anchorY, elbow, top + (font.Size / 2f), leader);
        canvas.DrawLine(elbow, top + (font.Size / 2f), marginLeft - 4f, top + (font.Size / 2f), leader);
    }

    /// <summary>
    /// What a note says. The label names the problem and the reason explains it — and that is the
    /// whole of it, because nothing in this document hands the author a line to use instead (F40).
    /// </summary>
    private static string NoteText(Mark mark) =>
        string.IsNullOrWhiteSpace(mark.Reason) ? mark.Label : $"{mark.Label} — {mark.Reason}";

    // ---- The reader, plan and verdict pages (Release I) ----------------------------------------
    //
    // These four page kinds are drawn rather than flowed: each opens with a caps label under a
    // full-width rule and closes with a block pinned to the foot, which is a shape the Heading /
    // Paragraph / Bullet vocabulary above cannot make. They are here rather than in the renderer
    // because they are geometry, and the renderer's job is to know what a page says.

    /// <summary>
    /// A page's own label, under a rule across the whole measure: the apparatus naming what this
    /// sheet is. Optionally a second label at the right — the reader's question, which on a reader's
    /// page is the other half of the same line.
    /// </summary>
    public void PageLabel(string left, string? right = null)
    {
        var baseline = y + PaperTypeScale.Label;
        Label(left, ContentLeft, baseline);
        if (!string.IsNullOrWhiteSpace(right))
        {
            Label(right, ContentRight - LabelWidth(right), baseline);
        }
        y = baseline + 6f;
        Rule(ContentLeft, y, ContentWidth);
        y += PageGeometry.RuleGapBelow;
    }

    /// <summary>
    /// One line of the readers' contents: who read it, from where, their verdict in a clause, and
    /// the page to turn to.
    /// </summary>
    public void ContentsRow(int index, string name, string lens, string verdict, int folio)
    {
        using var heading = ReportType.Heading();
        using var body = ReportType.Body();
        using var small = ReportType.Small();

        var numberWidth = 16f;
        var folioWidth = 34f;
        var textLeft = ContentLeft + numberWidth;
        var textWidth = ContentWidth - numberWidth - folioWidth - 10f;

        var lines = SkiaText.Wrap(body, verdict, textWidth);
        var height = heading.Size * PageGeometry.LineSpacing
            + (lines.Count * body.Size * PageGeometry.LineSpacing) + 6f;
        KeepTogether(height);

        var top = y;
        using var ink = new SKPaint { Color = ReportPalette.Ink, IsAntialias = true };
        using var muted = new SKPaint { Color = ReportPalette.InkMuted, IsAntialias = true };

        canvas.DrawText(
            index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ContentLeft, top + heading.Size, small, muted);
        canvas.DrawText(name, textLeft, top + heading.Size, heading, ink);
        Label(lens, ContentRight - folioWidth - 8f - LabelWidth(lens), top + heading.Size);
        canvas.DrawText(
            $"p. {folio}", ContentRight - folioWidth, top + heading.Size, small, muted);

        y = top + (heading.Size * PageGeometry.LineSpacing);
        foreach (var line in lines)
        {
            canvas.DrawText(line, textLeft, y + body.Size, body, ink);
            y += body.Size * PageGeometry.LineSpacing;
        }
        y += 4f;
    }

    /// <summary>
    /// A reader's own pros and cons, side by side under their two labels.
    ///
    /// <para>The tick and the cross are the <b>only</b> such marks in the report, and they are right
    /// here for a reason DESIGN.md §16 would otherwise forbid: these are one reader's preferences,
    /// not the book's severity tiers, so a mark that says "this one liked it" is a fact about the
    /// reader rather than a rating of the book.</para>
    /// </summary>
    public void ProsAndCons(
        string leftLabel, IReadOnlyList<string> left, string rightLabel, IReadOnlyList<string> right)
    {
        using var small = ReportType.Small();
        var gutter = 22f;
        var column = (ContentWidth - gutter) / 2f;
        var rightLeft = ContentLeft + column + gutter;

        var baseline = y + PaperTypeScale.Label;
        Label(leftLabel, ContentLeft, baseline, ReportPalette.Great);
        Label(rightLabel, rightLeft, baseline, ReportPalette.MustFix);
        y = baseline + 8f;

        var top = y;
        var leftBottom = Marked(left, ContentLeft, top, column, small, ReportPalette.Great, tick: true);
        var rightBottom = Marked(right, rightLeft, top, column, small, ReportPalette.MustFix, tick: false);
        y = Math.Max(leftBottom, rightBottom);
    }

    // One column of marked items; returns the y it finished at, so the pair can end level.
    private float Marked(
        IReadOnlyList<string> items, float left, float top, float width, SKFont font, SKColor mark, bool tick)
    {
        var indent = 14f;
        var cursor = top;
        using var paint = new SKPaint { Color = ReportPalette.Ink, IsAntialias = true };
        using var stroke = new SKPaint
        {
            Color = mark, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.4f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };

        foreach (var item in items)
        {
            foreach (var (line, i) in SkiaText.Wrap(font, item, width - indent).Select((line, i) => (line, i)))
            {
                if (i == 0)
                {
                    DrawMark(left, cursor + 3f, stroke, tick);
                }
                canvas.DrawText(line, left + indent, cursor + font.Size, font, paint);
                cursor += font.Size * PageGeometry.LineSpacing;
            }
            cursor += 4f;
        }

        return cursor;
    }

    private void DrawMark(float x, float top, SKPaint stroke, bool tick)
    {
        var size = 7f;
        if (tick)
        {
            using var path = new SKPath();
            path.MoveTo(x, top + (size * 0.55f));
            path.LineTo(x + (size * 0.35f), top + size);
            path.LineTo(x + size, top);
            canvas.DrawPath(path, stroke);
            return;
        }

        canvas.DrawLine(x, top, x + size, top + size, stroke);
        canvas.DrawLine(x + size, top, x, top + size, stroke);
    }

    /// <summary>
    /// One revision item: an empty check box, what to do, where, and the severity chip at the right.
    ///
    /// <para>The box is printed empty because this is the page that gets carried to the desk. The
    /// chip is at the right rather than leading the line because the list is ordered by <i>where the
    /// work is</i> — one pass through the manuscript, not three — and the tier still has to be
    /// readable down the page.</para>
    /// </summary>
    /// <summary>
    /// The heading to re-print at the top of a page this list continues onto, or null once the list
    /// is done with it. Set by <see cref="Heading"/>'s caller through <see cref="Continues"/>.
    /// </summary>
    private string? continuation;

    /// <summary>
    /// Says that the items which follow belong under <paramref name="heading"/>, so a page break in
    /// the middle of them re-prints it.
    ///
    /// <para>F42 asks for exactly this, and it is not a nicety. The revision plan spills overleaf
    /// whenever it is long, and without the repeat the continuation page opens with unheaded items
    /// whose only tier cue is the severity chip — which is colour alone, and DESIGN.md §16's
    /// non-colour-cue rule is the one thing this document may not lean on.</para>
    /// </summary>
    public void Continues(string? heading) => continuation = heading;

    public void CheckItem(string action, string citation, SKColor chip)
    {
        using var body = ReportType.Body();
        using var small = ReportType.Small();

        var indent = 18f;
        var chipWidth = 16f;
        var width = ContentWidth - indent - chipWidth;
        var lines = SkiaText.Wrap(body, action, width);
        var cited = string.IsNullOrWhiteSpace(citation)
            ? new List<string>()
            : SkiaText.Wrap(small, citation, width);

        var height = (lines.Count * body.Size * PageGeometry.LineSpacing)
            + (cited.Count * small.Size * PageGeometry.LineSpacing) + 10f;
        var before = PageCount;
        KeepTogether(height);

        // Carried over. Re-print the heading these items sit under, with "continued" after it so
        // the page says it is a continuation rather than a second list of the same name.
        if (PageCount != before && continuation is not null)
        {
            using var h3 = ReportType.Heading();
            Heading($"{continuation} (continued)", h3, ReportPalette.Ink);
        }

        var top = y;
        using var boxPaint = new SKPaint
        {
            Color = ReportPalette.InkMuted, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 1f,
        };
        canvas.DrawRoundRect(
            new SKRect(ContentLeft, top + 2f, ContentLeft + 9f, top + 11f), 1.4f, 1.4f, boxPaint);

        using var ink = new SKPaint { Color = ReportPalette.Ink, IsAntialias = true };
        using var muted = new SKPaint { Color = ReportPalette.InkMuted, IsAntialias = true };
        foreach (var line in lines)
        {
            canvas.DrawText(line, ContentLeft + indent, y + body.Size, body, ink);
            y += body.Size * PageGeometry.LineSpacing;
        }
        foreach (var line in cited)
        {
            canvas.DrawText(line, ContentLeft + indent, y + small.Size, small, muted);
            y += small.Size * PageGeometry.LineSpacing;
        }

        using var chipPaint = new SKPaint { Color = chip, IsAntialias = true };
        canvas.DrawRoundRect(
            new SKRect(ContentRight - 9f, top + 3f, ContentRight, top + 12f), 2f, 2f, chipPaint);

        y += 5f;
    }

    /// <summary>
    /// The count line that opens the revision plan: the tally in numerals, and what the ordering
    /// means beside it. Three Musts is a draft in good shape, and saying so in figures is less
    /// flattering and more useful than saying so in a sentence.
    /// </summary>
    public void Tally(IReadOnlyList<(int Count, string Name)> counts, string note)
    {
        using var figures = ReportType.Section();
        using var small = ReportType.Small();

        var top = y;
        var cursor = ContentLeft;
        using var ink = new SKPaint { Color = ReportPalette.Ink, IsAntialias = true };
        foreach (var (count, name) in counts)
        {
            var text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            canvas.DrawText(text, cursor, top + figures.Size, figures, ink);
            cursor += figures.MeasureText(text) + 5f;
            Label(name, cursor, top + figures.Size);
            cursor += LabelWidth(name) + 20f;
        }

        y = top + (figures.Size * PageGeometry.LineSpacing);

        if (!string.IsNullOrWhiteSpace(note))
        {
            using var muted = new SKPaint { Color = ReportPalette.InkMuted, IsAntialias = true };
            foreach (var line in SkiaText.Wrap(small, note, ContentWidth))
            {
                canvas.DrawText(line, ContentLeft, y + small.Size, small, muted);
                y += small.Size * PageGeometry.LineSpacing;
            }
        }
        y += PageGeometry.BlockGap;
    }

    /// <summary>
    /// The block that closes a page, pinned to its foot as the drawings pin theirs with
    /// <c>margin-top:auto</c>: a hairline, an optional caps label, and a line or two under it.
    ///
    /// <para>It measures itself rather than taking a height, which is the whole reason it exists.
    /// Written as "work out how tall this is, then call ToFoot" it was wrong by one label the first
    /// time it ran — and being wrong by a few points does not look like a layout that is a few points
    /// off. The last line falls past the foot, the wrapper breaks to a fresh sheet, and the report
    /// grows a nearly blank page carrying half a sentence.</para>
    /// </summary>
    public void Footer(string? label, string text, SKFont font, SKColor color)
    {
        var height = (PageGeometry.HairlineGap * 2) + PageGeometry.HairlineHeight
            + Measure(text, font) + FootSlack;
        if (!string.IsNullOrWhiteSpace(label))
            height += PaperTypeScale.Label + 5f;

        ToFoot(height);
        Hairline();
        if (!string.IsNullOrWhiteSpace(label))
        {
            Label(label, ContentLeft, y + PaperTypeScale.Label);
            y += PaperTypeScale.Label + 5f;
        }
        Paragraph(text, font, color);
    }

    /// <summary>
    /// One line set against the right margin, at the current position. The other half of a colophon,
    /// and the one thing a left-flowing writer cannot do by wrapping.
    /// </summary>
    public void Right(string text, SKFont font, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, ContentRight - font.MeasureText(text), y + font.Size, font, paint);
        y += font.Size * PageGeometry.LineSpacing;
    }

    /// <summary>
    /// A hair of room past the measured height, so the final line of a pinned block cannot land
    /// exactly on the foot and be pushed to the next sheet by a rounding error.
    /// </summary>
    private const float FootSlack = 3f;

    /// <summary>
    /// Pins what follows to the foot of the page. The caller says how tall the block is; a block
    /// taller than the room left simply stays where it is rather than being pushed up over the text
    /// above it. Prefer <see cref="Footer"/>, which measures itself.
    /// </summary>
    public void ToFoot(float height)
    {
        var top = ContentBottom - height;
        if (top > y)
            y = top;
    }

    /// <summary>How tall this text will be when wrapped to the full measure — for <see cref="ToFoot"/>.</summary>
    public float Measure(string text, SKFont font, float width = ContentWidth) =>
        SkiaText.Wrap(font, text, width).Count * font.Size * PageGeometry.LineSpacing;

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
