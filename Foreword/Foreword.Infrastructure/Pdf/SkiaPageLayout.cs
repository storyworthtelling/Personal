using SkiaSharp;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

// How text is placed inside a box — mirrors the PdfSharp XStringFormats the composers used.
internal enum TextAlign { TopLeft, TopCenter, TopRight, Center }

// The bespoke Skia layout engine for Foreword's documents, replacing the PdfSharp-vector PdfLayout.
// It draws in POINT units by scaling each page canvas by RenderDpi/72, so every measurement and font size in
// the composers stays the same numbers as the old PdfSharp code — the page is US Letter in points exactly as
// before. Each page is an SKSurface snapshotted to a PNG; ToPdf() wraps the PNGs with the shared
// PdfVolumeComposer. Ground stays white (the newspaper look).
internal sealed class SkiaPageLayout : IDisposable
{
    public const double Margin = 54; // 0.75 inch in points

    private const int RenderDpi = 150;
    private const float Scale = RenderDpi / 72f;

    private readonly bool landscape;
    private readonly List<byte[]> pages = new();

    private SKSurface surface = null!;
    private SKCanvas canvas = null!;

    public double PageWidth { get; }
    public double PageHeight { get; }
    public double ContentWidth => PageWidth - 2 * Margin;
    public double BottomY => PageHeight - Margin;
    public double Y { get; private set; }

    public SkiaPageLayout(bool landscape = false)
    {
        this.landscape = landscape;
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
        SnapshotCurrent();
        surface.Dispose();
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

        using var paint = new SKPaint { Color = color ?? SKColors.Black, IsAntialias = true };
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
        SnapshotCurrent();
        surface.Dispose();
        var composer = new PdfVolumeComposer();
        return composer.Compose(pages, RenderDpi);
    }

    public void Dispose() => surface?.Dispose();

    // ---- internals ----
    private void StartPage()
    {
        var info = new SKImageInfo(
            (int)Math.Ceiling(PageWidth * Scale),
            (int)Math.Ceiling(PageHeight * Scale));
        surface = SKSurface.Create(info);
        canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale(Scale); // draw in point space from here on
        Y = Margin;
    }

    private void SnapshotCurrent()
    {
        canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        pages.Add(data.ToArray());
    }

    private static SKRect Rect(double x, double y, double w, double h) =>
        new((float)x, (float)y, (float)(x + w), (float)(y + h));

    // The page count produced so far (snapshotted pages plus the one in progress). For tests.
    public int PageCountInProgress => pages.Count + 1;
}
