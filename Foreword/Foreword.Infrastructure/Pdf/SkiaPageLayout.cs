using SkiaSharp;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

// How text is placed inside a box — mirrors the PdfSharp XStringFormats the composers used.
internal enum TextAlign { TopLeft, TopCenter, TopRight, Center }

// The bespoke Skia layout engine for Foreword's documents.
//
// It draws in POINT units, which is what a PDF's user space already is — so the composers' measurements and
// font sizes are the same numbers they have always been, and nothing here scales. Pages go straight to the
// shared VectorPdfSink, so the text in a delivered document IS text: selectable, searchable, and rendered by
// the tablet at whatever its panel actually is.
//
// It used to snapshot each page to a 150-DPI PNG and wrap the PNGs with PdfVolumeComposer — a picture of a
// page. That cost roughly ten times the bytes on a full page and was resampled twice on e-ink, which is what
// made small type look soft. The draw calls below are unchanged by the move; only where they land is
// different. Ground stays white (the newspaper look).
internal sealed class SkiaPageLayout : IDisposable
{
    public const double Margin = 54; // 0.75 inch in points

    // The sheets go to the owner's reMarkable Paper Pro. Ink, the muted ink and the hairline come
    // from the shared palette at that profile rather than from Skia's named colours: the bible's ink
    // is a warm near-black and its hairline a paper cream, and the pure black on pure grey the
    // composers used is a colder document that merely looks plausible.
    public static readonly SKColor Ink = PaperPalette.Resolve(PaperRole.Ink, DeviceColorProfile.PaperPro);
    public static readonly SKColor InkMuted = PaperPalette.Resolve(PaperRole.InkMuted, DeviceColorProfile.PaperPro);
    public static readonly SKColor InkSoft = PaperPalette.Resolve(PaperRole.InkSoft, DeviceColorProfile.PaperPro);
    public static readonly SKColor Hairline = PaperPalette.Resolve(PaperRole.Hairline, DeviceColorProfile.PaperPro);

    // The sheet itself. It was white — the newspaper look, decided rather than inherited — and the
    // owner has since chosen the device's own warm ground instead, so a page in the hand and the
    // same page on the tablet are the same colour rather than two shades of nearly-white lying
    // next to each other.
    public static readonly SKColor Ground = PaperPalette.Resolve(PaperRole.Ground, DeviceColorProfile.PaperPro);

    private readonly VectorPdfSink sink = new();

    private SKCanvas canvas = null!;

    public double PageWidth { get; }
    public double PageHeight { get; }
    public double ContentWidth => PageWidth - 2 * Margin;
    public double BottomY => PageHeight - Margin;
    public double Y { get; private set; }

    public SkiaPageLayout(bool landscape = false)
    {
        PageWidth = landscape ? 792 : 612;   // US Letter in points
        PageHeight = landscape ? 612 : 792;
        StartPage();
    }

    // ---- Fonts ----
    public static SKFont Sans(float size, bool bold = false)
    {
        var typeface = bold ? EmbeddedTypefaces.SansSemibold : EmbeddedTypefaces.SansRegular;
        return new SKFont(typeface, size) { Edging = SKFontEdging.Antialias, Subpixel = true };
    }

    public static SKFont Serif(float size, bool black = false)
    {
        var typeface = black ? EmbeddedTypefaces.SerifBlack : EmbeddedTypefaces.SerifSemibold;
        return new SKFont(typeface, size) { Edging = SKFontEdging.Antialias, Subpixel = true };
    }

    /// <summary>
    /// Pinyon Script — the owner's voice, and nothing else (DESIGN.md §4). Every other line on a
    /// sheet is Serif or Sans; this hand is what marks a sentence as theirs rather than the app's.
    /// </summary>
    public static SKFont Script(float size) =>
        new(EmbeddedTypefaces.Script, size) { Edging = SKFontEdging.Antialias, Subpixel = true };

    // ---- Pagination ----
    public void NewPageIfNeeded(double spaceNeeded)
    {
        if (Y + spaceNeeded > BottomY)
            NewPage();
    }

    /// <summary>
    /// Drawn at the foot of every page just before it closes — a folio, a running head.
    ///
    /// <para>A footer cannot be written after the fact: Skia closes a PDF page and it is gone, so
    /// there is no going back to stamp one on. It has to be drawn while the page is still open,
    /// which is what this is for. Set it before the page's content and it lands on that page and
    /// every one the content runs on to.</para>
    /// </summary>
    public Action<SkiaPageLayout>? Footer { get; set; }

    public void NewPage()
    {
        ClosePage();
        StartPage();
    }

    public void Advance(double amount) => Y += amount;

    public void MoveTo(double y) => Y = y;

    // ---- Text ----
    public float MeasureText(string text, SKFont font) => font.MeasureText(text);

    // Recommended line height for a font, matching PdfSharp's font.GetHeight() usage.
    public static double LineHeight(SKFont font, double lineSpacing = 1.0)
    {
        font.GetFontMetrics(out var m);
        return (-m.Ascent + m.Descent + m.Leading) * lineSpacing;
    }

    public void DrawText(string text, SKFont font,
        double x, double y, double w, double h, TextAlign align, SKColor? color = null) =>
        DrawText(text, font, Rect(x, y, w, h), align, color);

    public void DrawText(string text, SKFont font, SKRect rect, TextAlign align, SKColor? color = null)
    {
        if (string.IsNullOrEmpty(text))
            return;

        using var paint = new SKPaint { Color = color ?? Ink, IsAntialias = true };
        font.GetFontMetrics(out var m);
        var width = font.MeasureText(text);

        var x = align switch
        {
            TextAlign.TopCenter or TextAlign.Center => (float)(rect.Left + (rect.Width - width) / 2),
            TextAlign.TopRight => rect.Right - width,
            _ => rect.Left,
        };
        // Baseline: top-aligned sits an ascent below the box top; Center puts the text's optical middle at MidY.
        var baseline = align == TextAlign.Center
            ? rect.MidY - (m.Ascent + m.Descent) / 2
            : rect.Top - m.Ascent;

        canvas.DrawText(text, x, baseline, font, paint);
    }

    public void DrawCentered(string text, SKFont font, double height)
    {
        NewPageIfNeeded(height);
        DrawText(text, font, Rect(Margin, Y, ContentWidth, height), TextAlign.TopCenter);
        Advance(height);
    }

    public void DrawWrapped(string text, SKFont font, double lineSpacing = 1.4)
    {
        var lineH = LineHeight(font, lineSpacing);
        foreach (var line in SkiaText.Wrap(font, text, (float)ContentWidth))
        {
            NewPageIfNeeded(lineH);
            DrawText(line, font, Rect(Margin, Y, ContentWidth, lineH), TextAlign.TopLeft);
            Advance(lineH);
        }
    }

    /// <summary>
    /// Wraps text inside a box and returns the height it used. Bounded by <paramref name="maxLines"/>,
    /// with the last line truncated if it runs on — a box on a composed page has a size decided
    /// before the text is known, and text that quietly runs past it is not clipped by Skia: it is
    /// drawn, off the edge of the sheet or under whatever is painted next.
    /// </summary>
    public double MeasureWrappedIn(string text, SKFont font, double width,
        int maxLines, double lineSpacing = 1.15) =>
        Math.Min(SkiaText.Wrap(font, text, (float)width).Count, maxLines)
        * LineHeight(font, lineSpacing);

    public double DrawWrappedIn(string text, SKFont font, double x, double y, double width,
        int maxLines, SKColor? color = null, double lineSpacing = 1.15)
    {
        var lineHeight = LineHeight(font, lineSpacing);
        var lines = SkiaText.Wrap(font, text, (float)width);
        var drawn = 0.0;

        for (var i = 0; i < lines.Count && i < maxLines; i++)
        {
            var last = i == maxLines - 1 && lines.Count > maxLines;
            DrawText(last ? Truncate(lines[i], font, width) : lines[i], font,
                Rect(x, y + drawn, width, lineHeight), TextAlign.TopLeft, color);
            drawn += lineHeight;
        }

        return drawn;
    }

    public string Truncate(string text, SKFont font, double maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth || text.Length == 0)
            return text;
        var trimmed = text;
        while (trimmed.Length > 1 && font.MeasureText(trimmed + "…") > maxWidth)
            trimmed = trimmed[..^1];
        return trimmed + "…";
    }

    // ---- Vector primitives ----
    public void DrawLine(double x1, double y1, double x2, double y2, SKColor color, float width)
    {
        using var paint = new SKPaint { Color = color, StrokeWidth = width, IsAntialias = true, IsStroke = true };
        canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, paint);
    }

    public void DrawRect(double x, double y, double w, double h,
        SKColor? stroke = null, float strokeWidth = 1f, SKColor? fill = null)
    {
        var rect = Rect(x, y, w, h);
        if (fill is { } fillColor)
        {
            using var fillPaint = new SKPaint { Color = fillColor, IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawRect(rect, fillPaint);
        }
        if (stroke is { } strokeColor)
        {
            using var strokePaint = new SKPaint
            {
                Color = strokeColor, StrokeWidth = strokeWidth, IsAntialias = true, Style = SKPaintStyle.Stroke
            };
            canvas.DrawRect(rect, strokePaint);
        }
    }

    /// <summary>
    /// A rectangle with rounded corners on <b>one side only</b>, which is the binder-tab shape
    /// (§10): square on the page side so the seam closes, rounded on the outer side.
    /// </summary>
    public void DrawTabRect(double x, double y, double w, double h,
        float radius, bool roundRight, SKColor fill, SKColor stroke, float strokeWidth)
    {
        var rect = Rect(x, y, w, h);
        var zero = new SKPoint(0, 0);
        var round = new SKPoint(radius, radius);

        // Clockwise from the top left, as SKRoundRect takes them.
        var corners = roundRight
            ? new[] { zero, round, round, zero }
            : new[] { round, zero, zero, round };

        using var shape = new SKRoundRect();
        shape.SetRectRadii(rect, corners);

        using var fillPaint = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawRoundRect(shape, fillPaint);

        using var strokePaint = new SKPaint
        {
            Color = stroke, StrokeWidth = strokeWidth, IsAntialias = true, Style = SKPaintStyle.Stroke,
        };
        canvas.DrawRoundRect(shape, strokePaint);
    }

    /// <summary>
    /// Draws <paramref name="draw"/> rotated about a point, with the origin moved there — for a
    /// label that runs down a tab. The canvas state is saved and restored, so a caller cannot leave
    /// the page rotated for everything after it.
    /// </summary>
    public void Rotated(double x, double y, float degrees, Action draw)
    {
        canvas.Save();
        canvas.Translate((float)x, (float)y);
        canvas.RotateDegrees(degrees);
        draw();
        canvas.Restore();
    }

    /// <summary>
    /// A bitmap circle-masked into a disc with a hairline ring — the owner's medallion (§13).
    ///
    /// <para>Masked when drawn rather than pre-masked as an asset, so the source stays a plain
    /// square photograph and swapping it is dropping in a file.</para>
    /// </summary>
    public void DrawCircularBitmap(SKBitmap bitmap, double x, double y, double size)
    {
        var dest = Rect(x, y, size, size);
        var radius = (float)size / 2f;
        var centre = new SKPoint(dest.MidX, dest.MidY);

        canvas.Save();
        canvas.ClipPath(CirclePath(centre, radius), antialias: true);

        // DrawImage rather than DrawBitmap: only the image overloads take sampling options, and a
        // photograph scaled to 96 points without a resampler is a photograph with jagged edges.
        using var image = SKImage.FromBitmap(bitmap);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, dest, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        canvas.Restore();

        using var ring = new SKPaint
        {
            Color = Hairline, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f,
        };
        canvas.DrawCircle(centre, radius, ring);
    }

    private static SKPath CirclePath(SKPoint centre, float radius)
    {
        var path = new SKPath();
        path.AddCircle(centre.X, centre.Y, radius);
        return path;
    }

    public void DrawImage(byte[] png, double x, double y, double w, double h)
    {
        using var image = SKImage.FromEncodedData(png);
        if (image is null)
            return;
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, Rect(x, y, w, h),
            new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
    }

    // Reads a PNG's intrinsic size in POINTS (assuming 72-DPI-agnostic pixels scaled to fit), used to size
    // article images to the content width. Returns (width, height) in points at natural pixel proportions.
    public static (double Width, double Height) ImageSizePoints(byte[] png)
    {
        using var codec = SKCodec.Create(new MemoryStream(png));
        if (codec is null)
            return (0, 0);
        return (codec.Info.Width, codec.Info.Height);
    }

    // ---- PDF assembly ----
    /// <summary>
    /// Names this page as a link destination. Paired with <see cref="LinkTo"/> through
    /// <c>SectionTabs.Destination</c>, because the two ends agreeing is the whole of it: a link to
    /// a name nothing anchors is a perfectly valid PDF that does nothing when touched.
    /// </summary>
    public void Anchor(string destination) =>
        canvas.DrawNamedDestinationAnnotation(new SKPoint(0, 0), destination);

    /// <summary>
    /// Makes a rectangle of this page a link to a named destination. Coordinates are the top-left
    /// page points every composer is written in; Skia flips them into the PDF's own bottom-left
    /// space, which is why nothing here does it by hand.
    /// </summary>
    public void LinkTo(double x, double y, double w, double h, string destination) =>
        canvas.DrawLinkDestinationAnnotation(Rect(x, y, w, h), destination);

    public byte[] ToPdf()
    {
        ClosePage();
        return sink.Close();
    }

    /// <summary>
    /// Ends the page in progress, drawing the footer first. The cursor is saved and restored around
    /// it so a footer cannot move the caller's place — it is furniture, not content.
    /// </summary>
    private void ClosePage()
    {
        if (Footer is { } footer)
        {
            var y = Y;
            footer(this);
            Y = y;
        }

        sink.EndPage();
    }

    public void Dispose() => sink.Dispose();

    // ---- internals ----
    private void StartPage()
    {
        // No scale: a PDF's user space is already 1/72 inch, which is the unit every measurement in
        // the composers is written in. The raster path only scaled because a bitmap has pixels.
        canvas = sink.BeginPage((float)PageWidth, (float)PageHeight);
        canvas.Clear(Ground);
        Y = Margin;
    }

    private static SKRect Rect(double x, double y, double w, double h) =>
        new((float)x, (float)y, (float)(x + w), (float)(y + h));

    // The page count produced so far, including the one in progress. For tests.
    public int PageCountInProgress => sink.PageCount;
}
