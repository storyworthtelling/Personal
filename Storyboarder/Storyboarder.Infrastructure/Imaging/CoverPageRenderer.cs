using System.Reflection;
using SkiaSharp;
using Storyboarder.Domain.Design;

namespace Storyboarder.Infrastructure.Imaging;

/// <summary>
/// Front-matter pages for the generated PDFs (DESIGN.md §9): volume covers,
/// the analysis report cover, and the analysis table of contents. Front
/// matter is unnumbered — no folio, no footer — so content page numbers keep
/// matching the printed TOC with zero bookkeeping.
/// </summary>
public sealed class CoverPageRenderer
{
    private static readonly SKColor Paper = SKColor.Parse(DesignTokens.Paper);
    private static readonly SKColor Ink = SKColor.Parse(DesignTokens.Ink);
    private static readonly SKColor Accent = SKColor.Parse(DesignTokens.Accent);
    private static readonly SKColor Muted = SKColor.Parse(DesignTokens.InkMuted);

    private static readonly Lazy<SKBitmap> medallion = new(() =>
    {
        var assembly = typeof(CoverPageRenderer).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("owner.png", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Branding medallion (owner.png) is not embedded.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        return SKBitmap.Decode(stream)!;
    });

    // backgroundArt (optional) is a spoiler-free comic-style illustration for the volume: when supplied it
    // fills the page and the novel + chapter names are typeset over it in light ink on dark scrims; when
    // null (no art, or its generation failed) the cover falls back to the plain Paper & Ink treatment.
    public byte[] AnalysisCover(string reportTitle)
    {
        var width = DesignTokens.Pdf.PageWidth;
        var height = DesignTokens.Pdf.PageHeight;
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(Paper);
        PaperGrain.Draw(canvas, width, height);

        var contentLeft = DesignTokens.Pdf.SideMargin;
        var contentWidth = width - 2 * contentLeft;

        using var titleFont = new SKFont(EmbeddedTypefaces.SerifSemibold, DesignTokens.Pdf.CoverTitle);
        var y = height * 0.26f;
        y = DrawWrapped(canvas, reportTitle, titleFont, Ink, contentLeft, y, contentWidth);

        y += 20f;
        DrawRule(canvas, contentLeft, y);
        y += 72f;

        using var subtitle = new SKFont(EmbeddedTypefaces.SerifItalic, DesignTokens.Pdf.H3);
        using var mutedPaint = new SKPaint { Color = Muted, IsAntialias = true };
        canvas.DrawText("Revision report", contentLeft, y, subtitle, mutedPaint);

        DrawMedallion(canvas, width / 2f, height * 0.82f, DesignTokens.Pdf.CoverMedallionSize);
        return Encode(surface);
    }

    public IReadOnlyList<byte[]> AnalysisToc(IReadOnlyList<(string Title, int Page)> entries)
    {
        var width = DesignTokens.Pdf.PageWidth;
        var height = DesignTokens.Pdf.PageHeight;
        var contentLeft = DesignTokens.Pdf.SideMargin;
        var contentRight = width - DesignTokens.Pdf.SideMargin;
        var lineHeight = DesignTokens.Pdf.Body * DesignTokens.Pdf.LineSpacing * 1.3f;

        var pages = new List<byte[]>();
        var index = 0;
        while (index < entries.Count || pages.Count == 0)
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(Paper);
            PaperGrain.Draw(canvas, width, height);

            var y = DesignTokens.Pdf.TopMargin + DesignTokens.Pdf.H2;
            if (pages.Count == 0)
            {
                using var heading = new SKFont(EmbeddedTypefaces.SerifSemibold, DesignTokens.Pdf.H2);
                using var inkPaint = new SKPaint { Color = Ink, IsAntialias = true };
                canvas.DrawText("Contents", contentLeft, y, heading, inkPaint);
                DrawRule(canvas, contentLeft, y + 12f);
                y += DesignTokens.Pdf.AccentRuleGap + DesignTokens.Pdf.H2;
            }

            using var entryFont = new SKFont(EmbeddedTypefaces.SerifRegular, DesignTokens.Pdf.Body);
            using var entryPaint = new SKPaint { Color = Ink, IsAntialias = true };
            using var numberPaint = new SKPaint { Color = Muted, IsAntialias = true };
            var bottom = height - DesignTokens.Pdf.BottomMargin;
            while (index < entries.Count && y < bottom)
            {
                var (title, page) = entries[index];
                canvas.DrawText(title, contentLeft, y, entryFont, entryPaint);
                var number = page.ToString();
                var numberWidth = entryFont.MeasureText(number);
                canvas.DrawText(number, contentRight - numberWidth, y, entryFont, numberPaint);
                y += lineHeight;
                index++;
            }
            pages.Add(Encode(surface));
        }
        return pages;
    }

    private static float DrawWrapped(SKCanvas canvas, string text, SKFont font, SKColor color, float x, float y, float wrapWidth)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        foreach (var line in SkiaText.Wrap(font, text, wrapWidth))
        {
            canvas.DrawText(line, x, y, font, paint);
            y += font.Size * 1.15f;
        }
        return y;
    }

    private static void DrawRule(SKCanvas canvas, float left, float y)
    {
        using var rule = new SKPaint { Color = Accent, IsAntialias = false };
        canvas.DrawRect(left, y, 220f, DesignTokens.Pdf.AccentRuleHeight * 2, rule);
    }

    private static void DrawMedallion(SKCanvas canvas, float centerX, float centerY, float size)
    {
        var half = size / 2f;
        var dest = new SKRect(centerX - half, centerY - half, centerX + half, centerY + half);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(medallion.Value, dest, paint);
    }

    private static byte[] Encode(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
