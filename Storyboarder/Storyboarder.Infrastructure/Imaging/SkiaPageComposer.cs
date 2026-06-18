using Microsoft.Extensions.Options;
using SkiaSharp;
using Storyboarder.Domain.Imaging;
using Storyboarder.Infrastructure.Configuration;

namespace Storyboarder.Infrastructure.Imaging;

// Composites a page's generated panel images into a single page raster using SkiaSharp, drawing
// panel borders and typesetting dialogue/captions deterministically (the image model is told not to
// render text, because it letters unreliably).
public sealed class SkiaPageComposer : IPageComposer
{
    private const float FontSize = 18f;
    private const float Padding = 8f;

    private readonly PageLayoutOptions layout;
    private readonly PageLayoutCalculator calculator;

    public SkiaPageComposer(IOptions<PageLayoutOptions> options)
    {
        layout = options.Value;
        calculator = new PageLayoutCalculator(layout.Margin, layout.Gutter);
    }

    public byte[] Compose(IReadOnlyList<PanelArt> panels)
    {
        if (panels.Count == 0)
            throw new ArgumentException("A page must have at least one panel.", nameof(panels));

        var info = new SKImageInfo(layout.Width, layout.Height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var rects = calculator.Layout(new PageSize(layout.Width, layout.Height), panels.Count);
        using var border = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 3, Color = SKColors.Black, IsAntialias = true
        };

        for (int i = 0; i < panels.Count; i++)
        {
            var rect = rects[i];
            var dest = new SKRect((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));

            DrawPanelImage(canvas, panels[i].Image, dest);
            canvas.DrawRect(dest, border);

            if (!string.IsNullOrWhiteSpace(panels[i].Dialogue))
                DrawTextBox(canvas, panels[i].Dialogue!, dest, anchorTop: true, new SKColor(255, 255, 255));
            if (!string.IsNullOrWhiteSpace(panels[i].Caption))
                DrawTextBox(canvas, panels[i].Caption!, dest, anchorTop: false, new SKColor(255, 249, 196));
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawPanelImage(SKCanvas canvas, byte[] imageBytes, SKRect dest)
    {
        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap is null)
        {
            using var placeholder = new SKPaint { Color = SKColors.LightGray };
            canvas.DrawRect(dest, placeholder);
            return;
        }

        float scale = Math.Min(dest.Width / bitmap.Width, dest.Height / bitmap.Height);
        float width = bitmap.Width * scale;
        float height = bitmap.Height * scale;
        float x = dest.Left + (dest.Width - width) / 2;
        float y = dest.Top + (dest.Height - height) / 2;
        canvas.DrawBitmap(bitmap, new SKRect(x, y, x + width, y + height));
    }

    private static void DrawTextBox(SKCanvas canvas, string text, SKRect panel, bool anchorTop, SKColor fillColor)
    {
        using var font = new SKFont(SKTypeface.Default, FontSize);
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        float maxTextWidth = panel.Width - 4 * Padding;
        var lines = WrapText(text, font, maxTextWidth);
        float lineHeight = FontSize * 1.3f;
        float boxHeight = lines.Count * lineHeight + 2 * Padding;
        float boxWidth = panel.Width - 2 * Padding;
        float boxX = panel.Left + Padding;
        float boxY = anchorTop ? panel.Top + Padding : panel.Bottom - Padding - boxHeight;
        var boxRect = new SKRect(boxX, boxY, boxX + boxWidth, boxY + boxHeight);

        using var fill = new SKPaint { Color = fillColor, IsAntialias = true };
        using var stroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = SKColors.Black, IsAntialias = true
        };
        canvas.DrawRoundRect(boxRect, 8, 8, fill);
        canvas.DrawRoundRect(boxRect, 8, 8, stroke);

        float baseline = boxY + Padding + FontSize;
        foreach (var line in lines)
        {
            canvas.DrawText(line, boxX + Padding, baseline, font, textPaint);
            baseline += lineHeight;
        }
    }

    private static List<string> WrapText(string text, SKFont font, float maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length == 0 || font.MeasureText(candidate) <= maxWidth)
                current = candidate;
            else
            {
                lines.Add(current);
                current = word;
            }
        }

        if (current.Length > 0)
            lines.Add(current);

        return lines.Count == 0 ? [string.Empty] : lines;
    }
}
