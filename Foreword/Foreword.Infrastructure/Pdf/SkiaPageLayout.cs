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

    // ---- Pagination ----
    public void NewPageIfNeeded(double spaceNeeded)
    {
        if (Y + spaceNeeded > BottomY)
            NewPage();
    }

    public void NewPage()
    {
        sink.EndPage();
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
    public byte[] ToPdf()
    {
        sink.EndPage();
        return sink.Close();
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
