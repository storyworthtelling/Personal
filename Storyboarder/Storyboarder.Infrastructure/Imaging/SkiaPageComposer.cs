using Microsoft.Extensions.Options;
using SkiaSharp;
using Storyboarder.Domain.Design;
using Storyboarder.Domain.Imaging;
using Storyboarder.Infrastructure.Configuration;

namespace Storyboarder.Infrastructure.Imaging;

// Composites a page's generated panel images into a single page raster using SkiaSharp, drawing
// panel borders, typesetting dialogue/captions deterministically (the image model is told not to
// render text, because it letters unreliably), and adding the folio band. Styling follows the
// prestige-graphic-novel spec in DESIGN.md §7: paper page, ink borders, bright balloons,
// deep-paper captions, Serif folios.
public sealed class SkiaPageComposer : IPageComposer
{
    private static readonly SKColor Paper = SKColor.Parse(DesignTokens.Paper);
    private static readonly SKColor PaperBright = SKColor.Parse(DesignTokens.PaperBright);
    private static readonly SKColor PaperDeep = SKColor.Parse(DesignTokens.PaperDeep);
    private static readonly SKColor PaperShade = SKColor.Parse(DesignTokens.PaperShade);
    private static readonly SKColor Ink = SKColor.Parse(DesignTokens.Ink);
    private static readonly SKColor InkMuted = SKColor.Parse(DesignTokens.InkMuted);

    private readonly PageLayoutOptions layout;
    private readonly PageLayoutCalculator calculator;

    public SkiaPageComposer(IOptions<PageLayoutOptions> options)
    {
        layout = options.Value;
        calculator = new PageLayoutCalculator(
            new PageMargins(layout.TopMargin, layout.SideMargin, layout.BottomMargin), layout.Gutter);
    }

    public byte[] Compose(IReadOnlyList<PanelArt> panels, PageChrome chrome)
    {
        if (panels.Count == 0)
            throw new ArgumentException("A page must have at least one panel.", nameof(panels));

        var info = new SKImageInfo(layout.Width, layout.Height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(Paper);
        PaperGrain.Draw(canvas, layout.Width, layout.Height);

        var rects = calculator.Layout(new PageSize(layout.Width, layout.Height), panels.Count);
        // Axis-aligned border strokes stay crisp without antialiasing.
        using var border = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = DesignTokens.Comic.PanelStroke, Color = Ink, IsAntialias = false
        };

        for (int i = 0; i < panels.Count; i++)
        {
            var rect = rects[i];
            var dest = new SKRect((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));

            DrawPanelImage(canvas, panels[i].Image, dest);
            canvas.DrawRect(dest, border);

            if (!string.IsNullOrWhiteSpace(panels[i].Dialogue))
                DrawTextBox(canvas, panels[i].Dialogue!, dest, anchorTop: true);
            if (!string.IsNullOrWhiteSpace(panels[i].Caption))
                DrawTextBox(canvas, panels[i].Caption!, dest, anchorTop: false);
        }

        DrawFolio(canvas, chrome);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawPanelImage(SKCanvas canvas, byte[] imageBytes, SKRect dest)
    {
        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap is null)
        {
            using var placeholder = new SKPaint { Color = PaperShade };
            canvas.DrawRect(dest, placeholder);
            return;
        }

        // Letterbox fill behind scaled art, so off-ratio panels sit on shaded paper, not white.
        using var letterbox = new SKPaint { Color = PaperShade };
        canvas.DrawRect(dest, letterbox);

        float scale = Math.Min(dest.Width / bitmap.Width, dest.Height / bitmap.Height);
        float width = bitmap.Width * scale;
        float height = bitmap.Height * scale;
        float x = dest.Left + (dest.Width - width) / 2;
        float y = dest.Top + (dest.Height - height) / 2;
        canvas.DrawBitmap(bitmap, new SKRect(x, y, x + width, y + height));
    }

    // Dialogue: bright-paper balloon, Sans lettering, anchored top.
    // Caption: deep-paper box, Serif Italic, anchored bottom.
    private static void DrawTextBox(SKCanvas canvas, string text, SKRect panel, bool anchorTop)
    {
        var typeface = anchorTop ? EmbeddedTypefaces.SansRegular : EmbeddedTypefaces.SerifItalic;
        var fontSize = anchorTop ? DesignTokens.Comic.LetteringSize : DesignTokens.Comic.CaptionSize;
        var fillColor = anchorTop ? PaperBright : PaperDeep;
        var radius = anchorTop ? DesignTokens.Comic.BalloonRadius : DesignTokens.Comic.CaptionRadius;
        var padding = DesignTokens.Comic.BoxPadding;

        using var font = new SKFont(typeface, fontSize);
        using var textPaint = new SKPaint { Color = Ink, IsAntialias = true };

        float maxTextWidth = panel.Width - 4 * padding;
        var lines = SkiaText.Wrap(font, text, maxTextWidth);
        float lineHeight = fontSize * DesignTokens.Comic.LineSpacing;
        float boxHeight = lines.Count * lineHeight + 2 * padding;
        float boxWidth = panel.Width - 2 * padding;
        float boxX = panel.Left + padding;
        float boxY = anchorTop ? panel.Top + padding : panel.Bottom - padding - boxHeight;
        var boxRect = new SKRect(boxX, boxY, boxX + boxWidth, boxY + boxHeight);

        using var fill = new SKPaint { Color = fillColor, IsAntialias = true };
        using var stroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = DesignTokens.Comic.BoxStroke, Color = Ink, IsAntialias = true
        };
        canvas.DrawRoundRect(boxRect, radius, radius, fill);
        canvas.DrawRoundRect(boxRect, radius, radius, stroke);

        float baseline = boxY + padding + fontSize;
        foreach (var line in lines)
        {
            canvas.DrawText(line, boxX + padding, baseline, font, textPaint);
            baseline += lineHeight;
        }
    }

    // The folio band inside the bottom margin: running volume title bottom-left
    // in muted ink, page number bottom-right in ink, both Serif.
    private void DrawFolio(SKCanvas canvas, PageChrome chrome)
    {
        using var font = new SKFont(EmbeddedTypefaces.SerifRegular, DesignTokens.Comic.FolioSize);
        float baseline = layout.Height - (DesignTokens.Comic.FolioBand - DesignTokens.Comic.FolioSize) / 2;

        if (!string.IsNullOrWhiteSpace(chrome.VolumeTitle))
        {
            using var titlePaint = new SKPaint { Color = InkMuted, IsAntialias = true };
            canvas.DrawText(chrome.VolumeTitle, layout.SideMargin, baseline, font, titlePaint);
        }

        var pageNumber = chrome.PageNumber.ToString();
        using var numberPaint = new SKPaint { Color = Ink, IsAntialias = true };
        float numberWidth = font.MeasureText(pageNumber);
        canvas.DrawText(pageNumber, layout.Width - layout.SideMargin - numberWidth, baseline, font, numberPaint);
    }
}
