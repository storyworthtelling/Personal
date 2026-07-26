using Microsoft.Extensions.Options;
using SkiaSharp;
using Storyboarder.Domain.Design;
using Storyboarder.Domain.Imaging;
using Storyboarder.Domain.Model;
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
    private static readonly SKColor Accent = SKColor.Parse(DesignTokens.Accent);

    // Mitchell cubic resampling: high quality for both the up- and down-scaling a panel needs to fit its
    // slot, avoiding the blockiness/aliasing of the default nearest-neighbour sampling.
    private static readonly SKSamplingOptions HighQualitySampling = new(SKCubicResampler.Mitchell);

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

        var rects = calculator.Layout(
            new PageSize(layout.Width, layout.Height), panels.Select(p => p.Shot).ToList());
        // Axis-aligned border strokes stay crisp without antialiasing.
        using var border = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = DesignTokens.Comic.PanelStroke, Color = Ink, IsAntialias = false
        };

        for (int i = 0; i < panels.Count; i++)
        {
            // Split the cell so dialogue and caption get their own bands and never sit on the art.
            var slot = PanelSlotLayout.Compute(rects[i], panels[i].Dialogue, panels[i].Caption);
            var art = ToRect(slot.Art);

            DrawPanelImage(canvas, panels[i].Image, art);
            canvas.DrawRect(art, border);

            if (slot.Dialogue is { } dialogueBand)
                DrawTextBox(canvas, panels[i].Dialogue!, ToRect(dialogueBand), anchorTop: true, panels[i].DialogueKind);
            if (slot.Caption is { } captionBand)
                DrawTextBox(canvas, panels[i].Caption!, ToRect(captionBand), anchorTop: false);

            // A prominent sound effect is lettered over the art itself, comic-style — display type, not a
            // reserved band. Drawn last so it sits on top of the panel image.
            if (!string.IsNullOrWhiteSpace(panels[i].Sfx))
                DrawSfx(canvas, panels[i].Sfx!, art);
        }

        DrawFolio(canvas, chrome);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKRect ToRect(Storyboarder.Domain.Imaging.PanelRect r) =>
        new((float)r.X, (float)r.Y, (float)(r.X + r.Width), (float)(r.Y + r.Height));

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
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, new SKRect(x, y, x + width, y + height), HighQualitySampling, paint: null);
    }

    // Draws the typeset box that fills a reserved text band (so it sits in its own space, never on the
    // art). Dialogue: bright-paper balloon, Sans lettering, in the band above the art. Caption:
    // deep-paper box, Serif Italic, in the band below it. PanelSlotLayout sized the band to the wrapped
    // text, so the box fills the band; a band the layout had to clamp simply clips any overflow line.
    private static void DrawTextBox(SKCanvas canvas, string text, SKRect band, bool anchorTop, DialogueKind? kind = null)
    {
        var typeface = anchorTop ? EmbeddedTypefaces.SansRegular : EmbeddedTypefaces.SerifItalic;
        var fontSize = anchorTop ? DesignTokens.Comic.LetteringSize : DesignTokens.Comic.CaptionSize;
        var fillColor = anchorTop ? PaperBright : PaperDeep;
        var radius = anchorTop ? DesignTokens.Comic.BalloonRadius : DesignTokens.Comic.CaptionRadius;
        var padding = DesignTokens.Comic.BoxPadding;

        using var font = new SKFont(typeface, fontSize);
        using var textPaint = new SKPaint { Color = Ink, IsAntialias = true };

        float maxTextWidth = band.Width - 4 * padding;
        var lines = SkiaText.Wrap(font, text, maxTextWidth);
        float lineHeight = fontSize * DesignTokens.Comic.LineSpacing;

        float boxX = band.Left + padding;
        float boxWidth = band.Width - 2 * padding;
        var boxRect = new SKRect(boxX, band.Top, boxX + boxWidth, band.Bottom);

        using var fill = new SKPaint { Color = fillColor, IsAntialias = true };
        using var stroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = DesignTokens.Comic.BoxStroke, Color = Ink, IsAntialias = true
        };
        canvas.DrawRoundRect(boxRect, radius, radius, fill);
        canvas.DrawRoundRect(boxRect, radius, radius, stroke);

        // Spoken dialogue gets a tail pointing down into the scene; a thought balloon trails shrinking
        // bubbles toward the thinker instead. Captions (anchorTop false, no kind) are narration and take
        // neither. The speaker/thinker position isn't known, so both point down from just left of centre.
        switch (kind)
        {
            case DialogueKind.Speech:
                DrawBalloonTail(canvas, boxRect, fill, stroke);
                break;
            case DialogueKind.Thought:
                DrawThoughtTrail(canvas, boxRect, fill, stroke);
                break;
        }

        float baseline = band.Top + padding + fontSize;
        foreach (var line in lines)
        {
            if (baseline > boxRect.Bottom)
                break; // a clamped band can't fit every line; stop before drawing past the box
            canvas.DrawText(line, boxX + padding, baseline, font, textPaint);
            baseline += lineHeight;
        }
    }

    // Draws the downward tail of a speech balloon. The filled triangle covers the balloon's bottom stroke
    // between its base points (opening the balloon into the tail), then the two slanted sides are stroked to
    // finish the outline. Points down from ~40% across the balloon, angled slightly toward the panel centre.
    private static void DrawBalloonTail(SKCanvas canvas, SKRect box, SKPaint fill, SKPaint stroke)
    {
        float tailHeight = Math.Min(box.Height * 0.5f, box.Width * 0.12f);
        if (tailHeight < 4f)
            return; // a band too short to carry a legible tail just keeps the plain box

        float halfBase = tailHeight * 0.5f;
        float baseX = box.Left + box.Width * 0.4f;
        float apexX = baseX - tailHeight * 0.35f; // lean toward the balloon's centre for a natural hook
        float baseY = box.Bottom;
        float apexY = box.Bottom + tailHeight;

        using var tail = new SKPath();
        tail.MoveTo(baseX - halfBase, baseY);
        tail.LineTo(baseX + halfBase, baseY);
        tail.LineTo(apexX, apexY);
        tail.Close();
        canvas.DrawPath(tail, fill);

        // Stroke only the two sides — the base sits under the balloon and is meant to read as open.
        using var sides = new SKPath();
        sides.MoveTo(baseX - halfBase, baseY);
        sides.LineTo(apexX, apexY);
        sides.LineTo(baseX + halfBase, baseY);
        canvas.DrawPath(sides, stroke);
    }

    // Draws the thought-bubble trail: a few shrinking circles descending from below the balloon toward the
    // thinker, the comics convention that reads as interior thought (in place of the speech balloon's tail).
    // Each is filled like the balloon and outlined in ink; they drift toward centre as they shrink.
    private static void DrawThoughtTrail(SKCanvas canvas, SKRect box, SKPaint fill, SKPaint stroke)
    {
        float maxRadius = Math.Min(box.Height * 0.22f, box.Width * 0.06f);
        if (maxRadius < 3f)
            return; // too small to read as a trail; leave the plain box

        float cx = box.Left + box.Width * 0.4f;
        float cy = box.Bottom + maxRadius; // a small gap below the balloon, then the first bubble
        float radius = maxRadius;
        for (var i = 0; i < 3; i++)
        {
            canvas.DrawCircle(cx, cy, radius, fill);
            canvas.DrawCircle(cx, cy, radius, stroke);
            cy += radius * 1.7f;
            radius *= 0.6f;
            cx -= maxRadius * 0.3f;
        }
    }

    // Letters a sound effect over the panel art as display type: Serif Semibold in terracotta with an ink
    // outline, tilted for energy, near the top-left of the art. Shrinks to fit if the word is long.
    private static void DrawSfx(SKCanvas canvas, string sfx, SKRect art)
    {
        var text = sfx.Trim().ToUpperInvariant();
        using var font = new SKFont(EmbeddedTypefaces.SerifSemibold, DesignTokens.Comic.SfxSize);

        float maxWidth = art.Width * 0.8f;
        float measured = font.MeasureText(text);
        if (measured > maxWidth && measured > 0)
            font.Size = DesignTokens.Comic.SfxSize * (maxWidth / measured);

        float x = art.Left + DesignTokens.Comic.BoxPadding * 2;
        float baseline = art.Top + font.Size + DesignTokens.Comic.BoxPadding;

        using var outline = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = DesignTokens.Comic.BoxStroke * 2, Color = Ink, IsAntialias = true
        };
        using var fillPaint = new SKPaint { Color = Accent, IsAntialias = true };

        canvas.Save();
        canvas.RotateDegrees(-8f, x, baseline);
        canvas.DrawText(text, x, baseline, font, outline);
        canvas.DrawText(text, x, baseline, font, fillPaint);
        canvas.Restore();
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
