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

    // The report's pictures are evidence, drawn a few inches wide on an e-ink page. Written losslessly,
    // 67 scene illustrations made a 173 MB file; as JPEG at this quality the whole report is a few tens.
    private const int PictureQuality = 80;

    private readonly VectorPdfSink sink;
    private readonly List<(string Title, int Page)> tocEntries = [];

    private readonly string header;
    private readonly IReadOnlyList<ReportSection> tabSections;
    private readonly IReadOnlyList<Storyworthtelling.Paper.Binder.BinderTab> tabs;
    private readonly List<ReportSection> begun = [];
    private readonly List<int> readerFolios = [];

    private SKCanvas canvas = null!;
    private ReportSection? pageSection;
    private ReportSection? currentSection;
    private bool folioSuppressed;
    private int finishedPages;
    private float y;

    /// <param name="runningFoot">The book's title and draft: the header every page after the cover is signed under.</param>
    /// <param name="sections">The report's parts that have a tab, in order (a measuring pass passes none).</param>
    public PageWriter(string runningFoot, IReadOnlyList<ReportSection>? sections = null)
    {
        // The document names itself to a desktop or screen reader once it leaves the tablet (audit W9.3).
        sink = new VectorPdfSink(PictureQuality, $"{runningFoot}: revision report");
        header = runningFoot;
        tabSections = sections ?? [];
        tabs = FolioSheet.Tabs(tabSections);
        StartPage();
    }

    /// <summary>The parts of the report this writer has begun, in order — what a measuring pass hands the final one.</summary>
    public IReadOnlyList<ReportSection> SectionsBegun => begun;

    /// <summary>The folio each reader's page began on, in order — what a measuring pass hands the readers' contents.</summary>
    public IReadOnlyList<int> ReaderFolios => readerFolios;

    /// <summary>
    /// Opens a part of the report: anchors its tab's link on this page, and the page's tab shows it open. Called just
    /// before the part's first heading, and only when the part prints.
    /// </summary>
    public void BeginSection(ReportSection section)
    {
        begun.Add(section);
        // A part that starts at the top of a sheet is that sheet's open tab; one that starts lower down opens from the
        // next sheet, so a page shows the part it begins with.
        currentSection = section;
        if (OnFreshPage || pageSection is null)
        {
            pageSection = section;
        }
        canvas.DrawNamedDestinationAnnotation(new SKPoint(0, 0), section.Destination);
    }

    /// <summary>
    /// Where each finding was written, by its key (R15): the named destination a plan item links to, the
    /// folio it is on, and the order it was written in, so an item drawing on several goes to the first.
    /// </summary>
    private readonly Dictionary<string, (int Order, int Folio)> places = new(StringComparer.Ordinal);

    /// <summary>The named destination for a finding's place.</summary>
    public static string PlaceOf(string key) => "finding-" + key;

    /// <summary>Marks where a finding is written, the first time it is: a named destination at this point.</summary>
    public void MarkPlace(string key, float x, float top)
    {
        if (string.IsNullOrEmpty(key) || places.ContainsKey(key))
            return;

        places[key] = (places.Count, Folio);
        canvas.DrawNamedDestinationAnnotation(new SKPoint(x, top), PlaceOf(key));
    }

    /// <summary>The earliest-written of these findings, or null when none of them is written anywhere.</summary>
    public (string Destination, int Folio)? FirstPlace(IEnumerable<string> keys)
    {
        var found = (keys ?? [])
            .Where(places.ContainsKey)
            .Select(key => (Key: key, Place: places[key]))
            .OrderBy(entry => entry.Place.Order)
            .FirstOrDefault();
        return found.Key is null ? null : (PlaceOf(found.Key), found.Place.Folio);
    }

    /// <summary>Records that a reader's page starts on this sheet.</summary>
    public void BeginReader() => readerFolios.Add(Folio);

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

    /// <summary>
    /// One note in a chapter's list (R7): the text indented as a bullet's, led by an upright rule in
    /// the severity's colour and dash — the same bracket a margin note wears.
    /// </summary>
    public void SeverityItem(string text, SKFont font, ReadinessTier severity)
    {
        var lineHeight = font.Size * PageGeometry.LineSpacing;
        var lines = SkiaText.Wrap(font, text, ContentWidth - PageGeometry.BulletIndent);
        var cue = SeverityCue.Leader(severity);
        using var paint = new SKPaint { Color = ReportPalette.InkSoft, IsAntialias = true };
        using var bracket = new SKPaint
        {
            Color = ReportPalette.Severity(severity), IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)MarkupGeometry.BracketWidth, StrokeCap = SKStrokeCap.Butt,
            PathEffect = cue.IsSolid ? null : SKPathEffect.CreateDash([.. cue.Dash.Select(length => (float)length)], 0),
        };

        for (var i = 0; i < lines.Count; i++)
        {
            if (y + lineHeight > ContentBottom)
                NewPage();
            var x = ContentLeft + ((float)MarkupGeometry.BracketWidth / 2f);
            canvas.DrawLine(x, y + 1f, x, y + lineHeight - 1f, bracket);
            canvas.DrawText(lines[i], ContentLeft + PageGeometry.BulletIndent, y + font.Size, font, paint);
            y += lineHeight;
        }

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
    /// A field label in the muted ink, as written: sentence case and no letterspacing (owner's ruling, audit W8.5, held
    /// for the printed report in W12), the apparatus voice of DESIGN.md §4.
    /// </summary>
    public void Label(string text, float x, float baseline, SKColor? color = null)
    {
        using var font = ReportType.Label();
        using var paint = new SKPaint { Color = color ?? ReportPalette.InkMuted, IsAntialias = true };
        canvas.DrawText(text, x, baseline, font, paint);
    }

    public float LabelWidth(string text)
    {
        using var font = ReportType.Label();
        return font.MeasureText(text);
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
        var left = ContentLeft;
        var step = width / (curve.Count - 1);
        var xs = Enumerable.Range(0, curve.Count).Select(i => left + (i * step)).ToArray();

        // Each point's few words on what drives it (R17), in lanes above the chart so none prints over
        // another; a crowded chart takes more lanes rather than losing a label.
        using var labelFont = ReportType.Small();
        var words = curve.Select(point => Fit(point.Driver, labelFont, MaxLabelWidth)).ToArray();
        var labels = ChartLabels.Place(
            [.. xs.Select((x, i) => new LabelRequest(x, words[i].Length == 0 ? 0 : labelFont.MeasureText(words[i])))],
            labelFont.Spacing, left, left + width);
        var lanes = words.Any(word => word.Length > 0) ? ChartLabels.Lanes(labels) : 0;
        var labelBand = lanes * labelFont.Spacing + (lanes > 0 ? 6f : 0f);
        var axisBand = SagBandDepth + labelFont.Spacing + 2f;

        if (y + labelBand + height + axisBand > ContentBottom && y > ContentTop)
            NewPage();

        var bandTop = y;
        var bottom = y + labelBand + height;
        var overlay = Domain.Report.PacingChart.Overlay(curve, findings ?? []);

        using (var frame = new SKPaint
        {
            Color = ReportPalette.InkMuted, IsAntialias = true, StrokeWidth = 1, Style = SKPaintStyle.Stroke,
        })
            canvas.DrawLine(left, bottom, left + width, bottom, frame);

        var points = new SKPoint[curve.Count];
        for (var i = 0; i < curve.Count; i++)
        {
            var t = (float)Math.Clamp(curve[i].Tension, 0, 1);
            points[i] = new SKPoint(xs[i], bottom - t * (height - 3f));
        }

        // The labels, lane 0 nearest the chart, each with a hairline down to its point.
        using (var labelInk = new SKPaint { Color = ReportPalette.InkSoft, IsAntialias = true })
        using (var leader = new SKPaint
        {
            Color = ReportPalette.Hairline, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.6f,
        })
            for (var i = 0; i < curve.Count; i++)
            {
                if (words[i].Length == 0)
                    continue;
                var label = labels[i];
                var baseline = bandTop + ((lanes - 1 - label.Lane) * labelFont.Spacing) + labelFont.Size;
                canvas.DrawText(words[i], (float)label.Left, baseline, labelFont, labelInk);
                canvas.DrawLine(xs[i], baseline + 2f, xs[i], points[i].Y - 4f, leader);
            }

        // The chapter numbers under the rule, below the sag brackets.
        using (var axisInk = new SKPaint { Color = ReportPalette.InkSoft, IsAntialias = true })
            for (var i = 0; i < curve.Count; i++)
                canvas.DrawText(
                    Domain.Report.PacingChart.AxisLabel(curve[i], i), xs[i], bottom + SagBandDepth + labelFont.Size + 2f,
                    SKTextAlign.Center, labelFont, axisInk);

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

        y = bottom + axisBand;
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

    /// <summary>The widest a pacing label prints; longer words are cut at a word with an ellipsis.</summary>
    private const float MaxLabelWidth = 96f;

    /// <summary>A label cut to the width it may take, at a word, with an ellipsis when cut.</summary>
    private static string Fit(string? words, SKFont font, float width)
    {
        var text = (words ?? string.Empty).Trim();
        if (text.Length == 0 || font.MeasureText(text) <= width)
            return text;

        var cut = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (cut.Count > 1 && font.MeasureText(string.Join(' ', cut) + "…") > width)
            cut.RemoveAt(cut.Count - 1);
        return string.Join(' ', cut) + "…";
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
        // Each note keeps its own mark: several notes can share a line, and each is drawn in its own
        // severity and led to its own phrase (taking the line's first mark for all of them coloured
        // every later note on that line by the first one's severity).
        var wanted = new List<(int Line, double AnchorY, double Height)>();
        var wrapped = new Dictionary<int, List<string>>();
        var noteMarks = new List<(Mark Mark, int Stack)>();
        for (var i = first; i < last; i++)
        {
            var stack = 0;
            foreach (var mark in anchored[i])
            {
                var text = NoteText(mark);
                var wraps = SkiaText.Wrap(noteFont, text, noteWidth - 6f);
                wrapped[wanted.Count] = wraps;
                wanted.Add((i, (i - first) * lineHeight, (wraps.Count * noteFont.Spacing) + 6f));
                noteMarks.Add((mark, stack++));
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

                // Where each finding anchored here is written, for the plan's links (R15).
                foreach (var mark in anchored[i])
                    MarkPlace(mark.FindingKey, textLeft, baseline - prose.Size);

                foreach (var mark in drawnOn[i])
                    DrawMarkOn(manuscript, new LineSpan(i, span.Start, span.End), mark, textLeft, baseline, prose);
            }

        for (var n = 0; n < packed.Notes.Count; n++)
        {
            var note = packed.Notes[n];
            if (note.AnchorLine >= last)
                continue;   // its line went to the next page with it

            var line = wanted[n].Line;
            var anchor = lines[line];
            DrawNote(
                packed.Notes[n], wrapped[n], noteMarks[n].Mark, noteMarks[n].Stack,
                PhraseOn(manuscript, new LineSpan(line, anchor.Start, anchor.End), noteMarks[n].Mark, textLeft, prose),
                marginLeft, noteFont, origin + ((line - first) * lineHeight) + prose.Size, origin);
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
            canvas, mark, x, baseline, width, ReportPalette.Severity(mark.Severity),
            tail: run.End >= mark.EndOffset);
    }

    /// <summary>Where a mark's phrase starts and ends on its anchor line, in page points.</summary>
    private static (float Left, float Right) PhraseOn(string manuscript, LineSpan line, Mark mark, float left, SKFont prose)
    {
        if (MarkPlacement.RunOn(mark, line) is not { } run)
            return (left, left);

        var x = left + prose.MeasureText(manuscript[line.StartOffset..run.Start]);
        return mark.IsPoint ? (x, x) : (x, x + prose.MeasureText(manuscript[run.Start..run.End]));
    }

    /// <summary>
    /// The note itself: a severity bracket, the reason, and a leader back to the phrase it is about
    /// (R6). The bracket and leader take the severity's colour and its dash (R8) — the dash because on
    /// the rM2 a Must's rose and a Should's ochre print as the same grey.
    /// </summary>
    private void DrawNote(
        PlacedNote note, IReadOnlyList<string> wraps, Mark mark, int stack, (float Left, float Right) phrase,
        float marginLeft, SKFont font, float baseline, float origin)
    {
        var hue = ReportPalette.Severity(mark.Severity);
        var cue = SeverityCue.Leader(mark.Severity);
        var top = origin + (float)note.Top;

        using var stroke = new SKPaint
        {
            Color = hue, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)cue.Width,
            StrokeCap = SKStrokeCap.Butt, StrokeJoin = SKStrokeJoin.Miter,
            PathEffect = cue.IsSolid ? null : SKPathEffect.CreateDash([.. cue.Dash.Select(length => (float)length)], 0),
        };

        // The bracket: an upright beside the note, at the bracket's full width, broken like the leader.
        using (var bracket = stroke.Clone())
        {
            bracket.StrokeWidth = (float)MarkupGeometry.BracketWidth;
            var x = marginLeft - (float)MarkupGeometry.BracketInset - ((float)MarkupGeometry.BracketWidth / 2f);
            canvas.DrawLine(x, top, x, top + (float)note.Height - 6f, bracket);
        }

        using (var ink = new SKPaint { Color = ReportPalette.InkSoft, IsAntialias = true })
        {
            var y = top + font.Size;
            foreach (var wrap in wraps)
            {
                canvas.DrawText(wrap, marginLeft, y, font, ink);
                y += font.Spacing;
            }
        }

        var route = LeaderRoute.For(
            phrase.Left, phrase.Right, baseline,
            ContentLeft + MarkupGeometry.TextWidth + (MarkupGeometry.Gutter / 2),
            marginLeft - MarkupGeometry.BracketInset - MarkupGeometry.BracketWidth - 2,
            top + (font.Size / 2f), stack);
        using var path = new SKPath();
        path.MoveTo((float)route[0].X, (float)route[0].Y);
        foreach (var point in route.Skip(1))
            path.LineTo((float)point.X, (float)point.Y);
        canvas.DrawPath(path, stroke);
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
    /// <param name="reserve">Room kept at the foot of every sheet the block uses — the reader's "Would need", so it
    /// follows the last item rather than landing on a sheet of its own.</param>
    public void ProsAndCons(
        string leftLabel, IReadOnlyList<string> left, string rightLabel, IReadOnlyList<string> right, float reserve = 0f)
    {
        var limit = ContentBottom - reserve;
        using var small = ReportType.Small();
        var gutter = 22f;
        var column = (ContentWidth - gutter) / 2f;
        var rightLeft = ContentLeft + column + gutter;
        var line = small.Size * PageGeometry.LineSpacing;

        // The two columns go down the sheet item by item, and an item that would cross the foot goes to the next sheet,
        // under the labels again. They used to be drawn with no foot at all, so under a long read the lists ran off the
        // bottom of the page and were cut off (the owner's report).
        var nextLeft = 0;
        var nextRight = 0;
        var first = true;
        while (first || nextLeft < left.Count || nextRight < right.Count)
        {
            if (y + PaperTypeScale.Label + 8f + line > limit)
            {
                NewPage();
            }

            var baseline = y + PaperTypeScale.Label;
            var suffix = first ? string.Empty : " (continued)";
            Label(leftLabel + suffix, ContentLeft, baseline, ReportPalette.Great);
            Label(rightLabel + suffix, rightLeft, baseline, ReportPalette.MustFix);
            y = baseline + 8f;

            var top = y;
            var leftBottom = Marked(left, ref nextLeft, ContentLeft, top, column, limit, small, ReportPalette.Great, tick: true);
            var rightBottom = Marked(right, ref nextRight, rightLeft, top, column, limit, small, ReportPalette.MustFix, tick: false);
            y = Math.Max(leftBottom, rightBottom);
            first = false;

            if (nextLeft < left.Count || nextRight < right.Count)
            {
                NewPage();
            }
        }
    }

    /// <summary>
    /// One column of marked items from <paramref name="next"/> on, each whole, until the next would cross the foot;
    /// returns the y it finished at, so the pair can end level. An item taller than a fresh sheet is drawn anyway rather
    /// than held back for ever.
    /// </summary>
    private float Marked(
        IReadOnlyList<string> items, ref int next, float left, float top, float width, float limit, SKFont font, SKColor mark, bool tick)
    {
        var indent = 14f;
        var cursor = top;
        var lineHeight = font.Size * PageGeometry.LineSpacing;
        using var paint = new SKPaint { Color = ReportPalette.Ink, IsAntialias = true };
        using var stroke = new SKPaint
        {
            Color = mark, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.4f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };

        while (next < items.Count)
        {
            var lines = SkiaText.Wrap(font, items[next], width - indent);
            var height = lines.Count * lineHeight;
            if (cursor + height > limit && cursor > top)
            {
                break;
            }

            foreach (var (text, i) in lines.Select((text, i) => (text, i)))
            {
                if (i == 0)
                {
                    DrawMark(left, cursor + 3f, stroke, tick);
                }
                canvas.DrawText(text, left + indent, cursor + font.Size, font, paint);
                cursor += lineHeight;
            }
            cursor += 4f;
            next++;
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

    public void CheckItem(string action, string citation, SKColor chip, string? destination = null, bool ticked = false)
    {
        using var body = ReportType.Body();
        using var small = ReportType.Small();

        // The box is sized for a pen on the tablet (R15), so the text clears it.
        var indent = PageGeometry.PenBox + 9f;
        var chipWidth = 16f;
        var width = ContentWidth - indent - chipWidth;
        var lines = SkiaText.Wrap(body, action, width);
        var cited = string.IsNullOrWhiteSpace(citation)
            ? new List<string>()
            : SkiaText.Wrap(small, citation, width);

        var height = Math.Max(
            (lines.Count * body.Size * PageGeometry.LineSpacing) + (cited.Count * small.Size * PageGeometry.LineSpacing),
            PageGeometry.PenBox + 2f) + 10f;
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
            new SKRect(ContentLeft, top + 1f, ContentLeft + PageGeometry.PenBox, top + 1f + PageGeometry.PenBox),
            1.4f, 1.4f, boxPaint);

        // A fixed item's box comes already ticked (R19), in the Positive mark.
        if (ticked)
        {
            using var tick = new SKPaint
            {
                Color = ReportPalette.Great, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f,
                StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
            };
            var box = PageGeometry.PenBox;
            using var path = new SKPath();
            path.MoveTo(ContentLeft + (box * 0.22f), top + 1f + (box * 0.52f));
            path.LineTo(ContentLeft + (box * 0.42f), top + 1f + (box * 0.74f));
            path.LineTo(ContentLeft + (box * 0.80f), top + 1f + (box * 0.26f));
            canvas.DrawPath(path, tick);
        }

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

        // The words, not the box, carry the link: a pen ticking the box should tick it, not turn the page.
        y = Math.Max(y, top + PageGeometry.PenBox + 2f);
        if (destination is not null)
            canvas.DrawLinkDestinationAnnotation(
                new SKRect(ContentLeft + indent, top, ContentRight - chipWidth, y), destination);

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
        var height = FooterHeight(label, text, font);

        if (height > Remaining && !OnFreshPage)
        {
            NewPage();
        }
        ToFoot(height);
        Hairline();
        if (!string.IsNullOrWhiteSpace(label))
        {
            Label(label, ContentLeft, y + PaperTypeScale.Label);
            y += PaperTypeScale.Label + 5f;
        }
        Paragraph(text, font, color);
    }

    /// <summary>How tall <see cref="Footer"/> will be for this label and text, so a block above it can leave the room.</summary>
    public float FooterHeight(string? label, string text, SKFont font)
    {
        var height = (PageGeometry.HairlineGap * 2) + PageGeometry.HairlineHeight + Measure(text, font) + FootSlack;
        if (!string.IsNullOrWhiteSpace(label))
            height += PaperTypeScale.Label + 5f;
        return height;
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
        canvas = sink.BeginPage(FolioSheet.Page);
        pageSection = currentSection;
        Storyworthtelling.Paper.Binder.SheetFrame.Clear(canvas);
        PaperGrain.Draw(canvas, PageGeometry.PageWidth, PageGeometry.PageHeight, GrainScale);
        y = ContentTop;
    }

    private void FinishPage()
    {
        // The shared tablet frame: every tab on every page, this page's part open; the header and the folio on every
        // page but the cover, which keeps its own label and jacket.
        if (tabs.Count > 0)
        {
            var active = pageSection is null ? -1 : tabSections.ToList().IndexOf(pageSection);
            Storyworthtelling.Paper.Binder.SheetFrame.Binder(canvas, FolioSheet.Page, tabs, active, anchor: false);
        }
        if (!folioSuppressed)
        {
            Storyworthtelling.Paper.Binder.SheetFrame.Header(canvas, FolioSheet.Page, header, FolioSheet.Signature);
            Storyworthtelling.Paper.Binder.SheetFrame.Folio(
                canvas, FolioSheet.Page, pageSection?.Name ?? "Contents", Folio.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        folioSuppressed = false;
        finishedPages++;
        sink.EndPage();
    }
}
