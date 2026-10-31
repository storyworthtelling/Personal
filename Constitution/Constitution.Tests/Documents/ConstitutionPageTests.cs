using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;
using Storyworthtelling.Paper;

namespace Constitution.Tests.Documents;

/// <summary>
/// C-2.1.1: every document is drawn on the tablet's own 3:4 page, and the one large figure on a dose
/// card sits on a registered rung. Read back from the PDF, not from the constants.
/// </summary>
public class ConstitutionPageTests
{
    [Fact]
    public void ThePageIsTheTabletsOwnThreeByFour()
    {
        Assert.Equal(445f, ConstitutionPage.Width);
        Assert.Equal(594f, ConstitutionPage.Height);
        Assert.Equal(3f / 4f, ConstitutionPage.Width / ConstitutionPage.Height, 0.002f);
    }

    [Fact]
    public void EveryPageBegunThroughTheSinkIsThatSize()
    {
        var sink = new VectorPdfSink();
        var canvas = ConstitutionPageSink.Begin(sink);
        canvas.Clear(SkiaSharp.SKColors.White);
        sink.EndPage();

        using var document = PdfReadBack.Open(sink.Close());
        var page = document.GetPage(1);

        Assert.Equal(445d, page.Width, 1);
        Assert.Equal(594d, page.Height, 1);
    }

    [Fact]
    public void ARenderedDocumentIsAllPagesAtThatSize()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(DocumentSamples.Pages()));

        Assert.Equal(2, document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            Assert.Equal(445d, page.Width, 1);
            Assert.Equal(594d, page.Height, 1);
        }
    }

    [Fact]
    public void TheDoseIsDrawnAtTheDoseRung()
    {
        var layout = DocumentSamples.ExercisePage();
        var dose = layout.Printed.Single(item => item.Role == Constitution.Domain.Documents.PrintedRole.Dose);
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([layout]));
        var page = document.GetPage(1);
        var letters = page.Letters.Where(l => PdfReadBack.Flip(page, l.GlyphRectangle).Overlaps(dose.Bounds)).ToList();

        Assert.NotEmpty(letters);
        Assert.All(letters, l => Assert.Equal(PaperTypeScale.Dose, (float)l.PointSize, 0.3f));
    }

    [Fact]
    public void NothingElseOnThePageIsAsLargeAsTheDose()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(DocumentSamples.Pages()));

        foreach (var page in document.GetPages())
        {
            var big = page.Letters.Where(l => l.PointSize > PaperTypeScale.Title + 0.5).ToList();
            Assert.All(big, l => Assert.Equal(PaperTypeScale.Dose, (float)l.PointSize, 0.3f));
        }
    }

    [Fact]
    public void EverySizeDrawnIsARegisteredRung()
    {
        var rungs = typeof(PaperTypeScale)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(float) && f.Name != nameof(PaperTypeScale.Ratio))
            .Select(f => (float)f.GetRawConstantValue()!)
            .ToArray();

        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(DocumentSamples.Pages()));
        var sizes = document.GetPages().SelectMany(p => p.Letters).Select(l => (float)l.PointSize).Distinct().ToList();

        Assert.NotEmpty(sizes);
        foreach (var size in sizes)
        {
            Assert.Contains(rungs, rung => Math.Abs(rung - size) < 0.3f);
        }
    }
}
