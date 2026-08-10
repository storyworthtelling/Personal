using SkiaSharp;
using Storyboarder.Domain.Design;
using Storyboarder.Infrastructure.Imaging;

namespace Storyboarder.Tests.Imaging;

public class CoverPageRendererTests
{
    private readonly CoverPageRenderer renderer = new();

    [Fact]
    public void AnalysisCover_HasReportGeometry()
    {
        var cover = renderer.AnalysisCover("My Novel");

        using var bitmap = SKBitmap.Decode(cover)!;
        Assert.Equal(DesignTokens.Pdf.PageWidth, bitmap.Width);
        Assert.Equal(DesignTokens.Pdf.PageHeight, bitmap.Height);
    }

    [Fact]
    public void AnalysisToc_PaginatesLongContents()
    {
        var few = renderer.AnalysisToc([("Logline", 1), ("Synopsis", 1)]);
        Assert.Single(few);

        var many = renderer.AnalysisToc(Enumerable.Range(1, 120)
            .Select(i => ($"Section {i}", i)).ToList());
        Assert.True(many.Count > 1, "expected a long contents to paginate");
    }
}
