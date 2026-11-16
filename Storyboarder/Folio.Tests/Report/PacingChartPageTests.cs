using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>The pacing chart on the page: numbered chapters, labelled points, and only where it belongs.</summary>
public class PacingChartPageTests
{
    private static readonly IReadOnlyList<ChapterTension> Curve =
    [
        new("Chapter 1", 0.3) { Driver = "Arrival, slow" },
        new("Chapter 2", 0.7) { Driver = "The padlock" },
        new("Chapter 3", 0.9) { Driver = "Storm at sea" },
    ];

    [Fact]
    public void ANovelsChartNumbersItsChaptersAndLabelsEachPoint()
    {
        var text = TextOf(Render(90_000));

        Assert.Contains("Tension across the chapters", text, StringComparison.Ordinal);
        Assert.Contains("The padlock", text, StringComparison.Ordinal);
        Assert.Contains("Storm at sea", text, StringComparison.Ordinal);
        Assert.Contains("Arrival, slow", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AShortStoryHasNoChart()
    {
        Assert.DoesNotContain("Tension across the chapters", TextOf(Render(6_000)), StringComparison.Ordinal);
    }

    [Fact]
    public void ANovellaNeedsThreeChapters()
    {
        Assert.Contains("Tension across the chapters", TextOf(Render(30_000)), StringComparison.Ordinal);
        Assert.DoesNotContain("Tension across the chapters", TextOf(Render(30_000, Curve.Take(2).ToList())), StringComparison.Ordinal);
    }

    private static byte[] Render(int words, IReadOnlyList<ChapterTension>? curve = null) =>
        new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of() with { WordCount = words, PacingCurve = curve ?? Curve },
            new ReportCover("The Salt Line", 1, words, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null),
            []);

    private static string TextOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return string.Join(" ", document.GetPages().Select(page => string.Join(" ", page.GetWords().Select(word => word.Text))));
    }
}
