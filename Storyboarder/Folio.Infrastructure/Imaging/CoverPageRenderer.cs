using Folio.Domain.Imaging;
using Folio.Domain.Report;
using SkiaSharp;
using Storyworthtelling.Paper;

namespace Folio.Infrastructure.Imaging;

/// <summary>
/// The report's front matter: the cover and the contents. Both are unnumbered, so the content folios
/// stay 1..N and the printed contents needs no bookkeeping.
///
/// <para>The cover is drawn 1:1 from the approved page: a label and its rule, the book's own jacket
/// in a keyline-framed slot, the title under it, and the owner's mark at the foot beside who the
/// report is for. The mark takes the jacket's own two most prominent tones, so a new jacket
/// recolours it with nobody choosing a colour (F37).</para>
/// </summary>
internal sealed class CoverPageRenderer
{
    public void AnalysisCover(PageWriter writer, ReportCover cover)
    {
        writer.SuppressFolio();
        var canvas = writer.Canvas;

        writer.Label("Folio · revision report", PageGeometry.ContentLeft,
            PageGeometry.ContentTop + PaperTypeScale.Label);
        var ruleTop = PageGeometry.ContentTop + PaperTypeScale.Label + PageGeometry.RuleGapAbove;
        writer.Rule(PageGeometry.ContentLeft, ruleTop, PageGeometry.ContentWidth);

        // The jacket, framed. Never bled: CoverArtSlot places it inside the column and the keyline is
        // what says the page is about a book rather than being one.
        var slot = CoverArtSlot.Place(ruleTop + 26f);
        var rect = new SKRect(slot.Left, slot.Top, slot.Right, slot.Bottom);
        var jacket = cover.Jacket is { Length: > 0 }
            && SkiaImageFit.DrawCover(canvas, cover.Jacket, rect, Crop);
        if (!jacket)
        {
            using var shade = new SKPaint { Color = ReportPalette.Hairline, IsAntialias = false };
            canvas.DrawRect(rect, shade);
        }
        using (var keyline = new SKPaint
        {
            Color = ReportPalette.Hairline, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = CoverArtSlot.KeylineWidth,
        })
            canvas.DrawRect(rect, keyline);

        var y = slot.Bottom + 30f;
        using (var title = ReportType.Title())
        {
            foreach (var line in SkiaText.Wrap(title, cover.Title, PageGeometry.ContentWidth))
            {
                y += title.Size;
                Centred(canvas, line, title, ReportPalette.Ink, y);
                y += title.Size * 0.06f;
            }
        }

        using (var draft = ReportType.Subtitle())
        {
            y += 12f + draft.Size;
            Centred(canvas, DraftLine(cover), draft, ReportPalette.InkSoft, y);
        }
        using (var read = ReportType.Small())
        {
            y += 2f + read.Size;
            Centred(canvas, $"Read {cover.ReadOn:d MMMM yyyy}", read, ReportPalette.InkSoft, y);
        }

        // The mark, at the foot of the column — §12 permits it here and nowhere in the app.
        const float markSize = 74f;
        var markTop = PageGeometry.ContentBottom - markSize;
        OwnersMark.Draw(canvas, new SKRect(
            PageGeometry.ContentLeft, markTop, PageGeometry.ContentLeft + markSize, markTop + markSize),
            JacketTones.Read(cover.Jacket));

        var textLeft = PageGeometry.ContentLeft + markSize + 12f;
        using (var forFont = ReportType.Small())
        {
            canvas.DrawText("Prepared for the author", textLeft, markTop + markSize / 2f,
                forFont, new SKPaint { Color = ReportPalette.Ink, IsAntialias = true });
            if (!string.IsNullOrWhiteSpace(cover.SeriesLine))
            {
                canvas.DrawText(cover.SeriesLine, textLeft,
                    markTop + markSize / 2f + forFont.Size * 1.4f,
                    forFont, new SKPaint { Color = ReportPalette.InkSoft, IsAntialias = true });
            }
        }
    }

    public void AnalysisToc(PageWriter writer, IReadOnlyList<(string Title, int Page)> entries)
    {
        writer.NewPage();

        writer.Label("Contents", PageGeometry.ContentLeft, PageGeometry.ContentTop + PaperTypeScale.Label);
        writer.Rule(PageGeometry.ContentLeft,
            PageGeometry.ContentTop + PaperTypeScale.Label + PageGeometry.RuleGapAbove,
            PageGeometry.ContentWidth);
        writer.Y = PageGeometry.ContentTop + PaperTypeScale.Label + PageGeometry.RuleGapBelow * 2;

        using var entryFont = ReportType.Body();
        using var folioFont = ReportType.Folio();
        using var entryPaint = new SKPaint { Color = ReportPalette.Ink, IsAntialias = true };
        using var numberPaint = new SKPaint { Color = ReportPalette.InkMuted, IsAntialias = true };
        var lineHeight = entryFont.Size * PageGeometry.LineSpacing * 1.3f;

        foreach (var (title, page) in entries)
        {
            if (writer.Y + lineHeight > PageGeometry.ContentBottom)
            {
                writer.NewPage();
                writer.Y = PageGeometry.ContentTop;
            }
            var baseline = writer.Y + entryFont.Size;
            writer.Canvas.DrawText(title, PageGeometry.ContentLeft, baseline, entryFont, entryPaint);
            var number = page.ToString();
            writer.Canvas.DrawText(number, PageGeometry.ContentRight - folioFont.MeasureText(number),
                baseline, folioFont, numberPaint);
            writer.Y += lineHeight;
        }
    }

    private static string DraftLine(ReportCover cover)
    {
        var draft = $"Draft {Ordinal(cover.DraftNumber)}";
        return cover.WordCount > 0 ? $"{draft} · {cover.WordCount:N0} words" : draft;
    }

    private static string Ordinal(int draft) => draft switch
    {
        1 => "one", 2 => "two", 3 => "three", 4 => "four", 5 => "five",
        6 => "six", 7 => "seven", 8 => "eight", 9 => "nine", 10 => "ten",
        _ => draft.ToString(),
    };

    private static SKRect Crop(int width, int height)
    {
        var crop = CoverArtSlot.Crop(width, height);
        return new SKRect(crop.Left, crop.Top, crop.Right, crop.Bottom);
    }

    private static void Centred(SKCanvas canvas, string text, SKFont font, SKColor color, float baseline)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, PageGeometry.PageWidth / 2f - font.MeasureText(text) / 2f, baseline, font, paint);
    }
}
