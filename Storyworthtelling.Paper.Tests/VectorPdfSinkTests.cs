using SkiaSharp;
using UglyToad.PdfPig;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// The vector sink, checked by reading the PDF back rather than by trusting the call.
///
/// <para>Every document in the suite has been a 150-DPI PNG per page wrapped by PdfSharp. That is a
/// <i>picture</i> of a page: nothing in it can be selected, searched or reflowed, it is enormous, and
/// on an e-ink panel it is resampled twice. The claim this release makes is that the same draw calls,
/// sent to a PDF canvas instead of a bitmap, produce real text — so the test extracts the text and
/// looks for it.</para>
/// </summary>
public class VectorPdfSinkTests
{
    private const float LetterWidth = 612;
    private const float LetterHeight = 792;

    /// <summary>US Letter in points, which is what the composers have always measured in.</summary>
    [Fact]
    public void PagesAreTheSizeTheyWereAskedFor()
    {
        using var document = PdfDocument.Open(new MemoryStream(OnePage("Tuesday")));

        var page = document.GetPage(1);
        Assert.Equal(LetterWidth, page.Width, 1);
        Assert.Equal(LetterHeight, page.Height, 1);
    }

    /// <summary>
    /// The whole point. A rasterised page contains no text at all — this passes only if the glyphs
    /// were emitted as text with a font behind them.
    /// </summary>
    [Fact]
    public void TheTextOnThePageIsRealText()
    {
        using var document = PdfDocument.Open(new MemoryStream(OnePage("Tuesday 6 September")));

        Assert.Contains("Tuesday", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPageBegunIsAPageInTheDocument()
    {
        var sink = new VectorPdfSink();
        foreach (var word in new[] { "one", "two", "three" })
        {
            var canvas = sink.BeginPage(LetterWidth, LetterHeight);
            Draw(canvas, word);
            sink.EndPage();
        }

        using var document = PdfDocument.Open(new MemoryStream(sink.Close()));
        Assert.Equal(3, document.NumberOfPages);
        Assert.Contains("three", document.GetPage(3).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The size claim from the technical finding, reproduced rather than cited — and reproduced on a
    /// page like the ones the app actually produces.
    ///
    /// <para><b>The first version of this test failed, and was right to.</b> It compared a nearly
    /// blank page carrying five words: vector 75,700 bytes against raster 17,442 — the vector four
    /// times <i>larger</i>. A PDF embeds its fonts, which is a fixed cost of roughly 75 KB paid once
    /// per document, while an almost-white PNG compresses to nearly nothing. Gate 2's measured
    /// 79.2 KB against 2,670.9 KB was five <i>full</i> pages, and the whole saving comes from that
    /// fixed cost being amortised while the raster's grows with every page and every mark on it.
    /// Measuring a sparse page proves the opposite of the thing being claimed.</para>
    /// </summary>
    [Fact]
    public void AVectorBriefIsAnOrderOfMagnitudeSmallerThanTheRasterItReplaces()
    {
        var vector = VectorBrief(Pages).Length;
        var raster = RasterBrief(Pages).Length;

        Assert.True(vector * 10 < raster,
            $"Vector was {vector} bytes and raster {raster} — the gap that justifies this change is gone.");
    }

    /// <summary>
    /// Why the saving grows with the document: the fonts are embedded once, so a five-page brief is
    /// nowhere near five times a one-page one. This is the mechanism behind the test above, pinned
    /// separately so a change that started embedding fonts per page would be named rather than just
    /// eroding the margin above until it failed.
    /// </summary>
    [Fact]
    public void TheEmbeddedFontIsAFixedCostPaidOncePerDocument()
    {
        var one = VectorBrief(1).Length;
        var five = VectorBrief(5).Length;

        Assert.True(five < one * 2,
            $"One page was {one} bytes and five were {five}. Five pages costing more than twice one "
            + "means the fonts are no longer being embedded once.");
    }

    /// <summary>
    /// Closing twice is a caller bug, and one that would otherwise produce a truncated or empty PDF
    /// depending on the platform rather than saying so.
    /// </summary>
    [Fact]
    public void ClosingTwiceSaysSoRatherThanReturningRubbish()
    {
        var sink = new VectorPdfSink();
        sink.BeginPage(LetterWidth, LetterHeight);
        sink.EndPage();
        sink.Close();

        Assert.Throws<InvalidOperationException>(() => { sink.Close(); });
    }

    [Fact]
    public void ADocumentWithNoPagesSaysSoRatherThanWritingAnEmptyFile()
    {
        Assert.Throws<InvalidOperationException>(() => { new VectorPdfSink().Close(); });
    }

    // ---- helpers ----

    private const int Pages = 5;

    private static byte[] OnePage(string text)
    {
        var sink = new VectorPdfSink();
        Draw(sink.BeginPage(LetterWidth, LetterHeight), text);
        sink.EndPage();
        return sink.Close();
    }

    private static byte[] VectorBrief(int pages)
    {
        var sink = new VectorPdfSink();
        for (var page = 0; page < pages; page++)
        {
            FillPage(sink.BeginPage(LetterWidth, LetterHeight), page);
            sink.EndPage();
        }

        return sink.Close();
    }

    /// <summary>The old path: draw the identical pages to 150-DPI bitmaps and wrap them.</summary>
    private static byte[] RasterBrief(int pages)
    {
        const int dpi = 150;
        const float scale = dpi / 72f;
        var sheets = new List<byte[]>();

        for (var page = 0; page < pages; page++)
        {
            var info = new SKImageInfo((int)(LetterWidth * scale), (int)(LetterHeight * scale));
            using var surface = SKSurface.Create(info);
            surface.Canvas.Scale(scale);
            FillPage(surface.Canvas, page);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            sheets.Add(data.ToArray());
        }

        return new PdfVolumeComposer().Compose(sheets, dpi);
    }

    /// <summary>
    /// A page with as much on it as a real brief page: forty-odd lines of body text under a heading.
    /// The comparison is only honest on a page like this.
    /// </summary>
    private static void FillPage(SKCanvas canvas, int page)
    {
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var heading = new SKFont(EmbeddedTypefaces.SerifSemibold, 18);
        using var body = new SKFont(EmbeddedTypefaces.SerifRegular, 11);

        canvas.DrawText($"The morning of the {page + 1}st", 54, 80, heading, paint);

        var y = 110f;
        for (var line = 0; line < 44; line++)
        {
            canvas.DrawText(
                $"Line {line} — the quick brown fox jumps over the lazy dog, and the paper takes it well.",
                54, y, body, paint);
            y += 15;
        }
    }

    private static void Draw(SKCanvas canvas, string text)
    {
        canvas.Clear(SKColors.White);
        using var font = new SKFont(EmbeddedTypefaces.SerifRegular, 24);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawText(text, 54, 100, font, paint);
    }
}
