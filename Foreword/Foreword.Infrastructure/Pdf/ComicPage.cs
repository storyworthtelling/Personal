using Foreword.Domain.Brief;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// The comic (B3) — last before the puzzle, which is where it belongs: the thing you read when you
/// have finished reading.
///
/// <para>The strip at its own size with a keyline, and the hover text set plainly underneath,
/// unlabelled. The hover text is on the page because <b>on paper there is nothing to hover</b>, and
/// it is the second joke — a printed xkcd without it is half a comic.</para>
/// </summary>
internal static class ComicPage
{
    private const double Keyline = 1;
    private const double Gap = 14;

    public static void Draw(SkiaPageLayout layout, ComicStrip strip, bool crosswordFollows)
    {
        var kickerFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var titleFont = SkiaPageLayout.Serif(PaperTypeScale.Heading);
        var altFont = SkiaPageLayout.Serif(PaperTypeScale.Body);
        var folioFont = SkiaPageLayout.Sans(PaperTypeScale.Label);

        // The folio says what comes next, because a comic is the last thing before the puzzle and a
        // reader who has just finished the news wants to know whether there is more paper coming.
        var folio = crosswordFollows ? "xkcd · then the crossword" : "xkcd";
        layout.Footer = page => page.DrawText(folio, folioFont,
            SkiaPageLayout.Margin, page.BottomY, page.ContentWidth,
            SkiaPageLayout.LineHeight(folioFont), TextAlign.TopLeft, SkiaPageLayout.InkMuted);

        layout.DrawWrapped($"XKCD · NO. {strip.Number}", kickerFont);
        layout.Advance(4);
        layout.DrawWrapped(strip.Title, titleFont);
        layout.Advance(Gap);

        DrawStrip(layout, strip);

        if (!string.IsNullOrWhiteSpace(strip.AltText))
        {
            layout.Advance(Gap);
            layout.DrawWrapped(strip.AltText, altFont, lineSpacing: 1.5);
        }
    }

    /// <summary>
    /// The strip at its own size — scaled DOWN to fit the measure when it is wider, never up. An
    /// xkcd enlarged past its pixels is a blurry xkcd, and most of them are narrower than the page.
    /// </summary>
    private static void DrawStrip(SkiaPageLayout layout, ComicStrip strip)
    {
        var (width, height) = SkiaPageLayout.ImageSizePoints(strip.Image);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var available = layout.ContentWidth - 2 * Keyline;
        var drawnWidth = Math.Min(width, available);
        var drawnHeight = height * drawnWidth / width;

        // Centred, because a strip narrower than the measure pinned to the left margin reads as a
        // layout that ran out rather than as a picture.
        var left = SkiaPageLayout.Margin + (layout.ContentWidth - drawnWidth) / 2;

        layout.DrawRect(left - Keyline, layout.Y - Keyline,
            drawnWidth + 2 * Keyline, drawnHeight + 2 * Keyline,
            stroke: SkiaPageLayout.Ink, strokeWidth: (float)Keyline);

        layout.DrawImage(strip.Image, left, layout.Y, drawnWidth, drawnHeight);
        layout.Advance(drawnHeight);
    }
}
