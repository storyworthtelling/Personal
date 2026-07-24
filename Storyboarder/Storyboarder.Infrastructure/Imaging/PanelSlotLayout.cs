using SkiaSharp;
using Storyboarder.Domain.Design;
using Storyboarder.Domain.Imaging;

namespace Storyboarder.Infrastructure.Imaging;

// A panel cell split into non-overlapping regions: the art in the middle, and — when the panel has
// them — a dialogue band above and a caption band below. Giving typeset text its own bands keeps it off
// the artwork (DESIGN.md §7) instead of painting balloons over the art. The composer draws into these
// rects, and the pipeline asks the image model to frame the panel art at the Art region's aspect ratio
// so the art fills its space with the bands reserved (the two must agree, so both go through here).
public sealed record PanelSlot(PanelRect Art, PanelRect? Dialogue, PanelRect? Caption);

public static class PanelSlotLayout
{
    // A single band never eats more than this fraction of the cell, and the art always keeps at least
    // this much of the cell height — so even a very long caption can't crush the artwork (its text just
    // wraps/clips within the band rather than growing without bound).
    private const double MaxBandFraction = 0.33;
    private const double MinArtFraction = 0.40;

    public static PanelSlot Compute(PanelRect cell, string? dialogue, string? caption)
    {
        var padding = DesignTokens.Comic.BoxPadding;
        var gap = DesignTokens.Comic.Gutter / 2.0; // breathing room between a band and the art

        var maxBand = cell.Height * MaxBandFraction;

        var dialogueBand = HasText(dialogue)
            ? Math.Min(maxBand, BandHeight(EmbeddedTypefaces.SansRegular, DesignTokens.Comic.LetteringSize, dialogue!, cell.Width, padding))
            : 0;
        var captionBand = HasText(caption)
            ? Math.Min(maxBand, BandHeight(EmbeddedTypefaces.SerifItalic, DesignTokens.Comic.CaptionSize, caption!, cell.Width, padding))
            : 0;

        // Reserve the art first: if the two bands together would leave the art below its floor, shrink
        // them proportionally so the art keeps at least MinArtFraction of the cell.
        var bandBudget = cell.Height * (1 - MinArtFraction);
        var bandsTotal = dialogueBand + captionBand;
        if (bandsTotal > bandBudget && bandsTotal > 0)
        {
            var scale = bandBudget / bandsTotal;
            dialogueBand *= scale;
            captionBand *= scale;
        }

        var artTop = cell.Y + (dialogueBand > 0 ? dialogueBand + gap : 0);
        var artBottom = cell.Y + cell.Height - (captionBand > 0 ? captionBand + gap : 0);

        var art = new PanelRect(cell.X, artTop, cell.Width, Math.Max(1, artBottom - artTop));
        var dialogueRect = dialogueBand > 0 ? new PanelRect(cell.X, cell.Y, cell.Width, dialogueBand) : null;
        var captionRect = captionBand > 0
            ? new PanelRect(cell.X, cell.Y + cell.Height - captionBand, cell.Width, captionBand)
            : null;

        return new PanelSlot(art, dialogueRect, captionRect);
    }

    private static bool HasText(string? text) => !string.IsNullOrWhiteSpace(text);

    // The height a wrapped text box needs in a band of the given width: wrapped line count times line
    // height, plus padding above and below — the same geometry the composer draws the box with.
    private static double BandHeight(SKTypeface typeface, float fontSize, string text, double bandWidth, float padding)
    {
        using var font = new SKFont(typeface, fontSize);
        var wrapWidth = (float)(bandWidth - 4 * padding);
        var lines = SkiaText.Wrap(font, text, wrapWidth);
        var lineHeight = fontSize * DesignTokens.Comic.LineSpacing;
        return lines.Count * lineHeight + 2 * padding;
    }
}
