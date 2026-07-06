using SkiaSharp;
using Storyboarder.Domain.Design;
using Storyboarder.Infrastructure.Imaging;

namespace Storyboarder.Tests.Imaging;

public class CoverPageRendererTests
{
    private readonly CoverPageRenderer renderer = new();

    [Fact]
    public void VolumeCover_HasComicPageGeometryAndGrainedPaper()
    {
        var cover = renderer.VolumeCover("The Lighthouse Keeper", 2, "The Harbour Debt");

        using var bitmap = SKBitmap.Decode(cover)!;
        Assert.Equal(DesignTokens.Comic.PageWidth, bitmap.Width);
        Assert.Equal(DesignTokens.Comic.PageHeight, bitmap.Height);
        SkiaPageComposerTests.AssertNearPaper(bitmap.GetPixel(5, 5));
    }

    [Fact]
    public void VolumeCover_CarriesTitleInkAndTerracottaRule()
    {
        var cover = renderer.VolumeCover("The Lighthouse Keeper", 2, "The Harbour Debt");

        using var bitmap = SKBitmap.Decode(cover)!;
        var accent = SKColor.Parse(DesignTokens.Accent);
        var inked = 0;
        var accented = 0;
        for (var x = 0; x < bitmap.Width; x += 2)
        {
            for (var y = 0; y < bitmap.Height; y += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (SkiaPageComposerTests.IsStrongInk(pixel))
                {
                    inked++;
                }
                if (pixel == accent)
                {
                    accented++;
                }
            }
        }
        Assert.True(inked > 500, $"expected the serif title, found {inked} ink pixels");
        Assert.True(accented > 50, $"expected the terracotta rule, found {accented} accent pixels");
    }

    [Fact]
    public void VolumeCover_CarriesTheMedallion()
    {
        var cover = renderer.VolumeCover("A", 1, "");

        using var bitmap = SKBitmap.Decode(cover)!;
        // The medallion sits centered at ~84% height; its monogram (or the
        // owner's photo) puts strong ink somewhere in that region.
        var centerX = bitmap.Width / 2;
        var centerY = (int)(bitmap.Height * 0.84f);
        var half = (int)(DesignTokens.Comic.CoverMedallionSize / 2);
        var found = false;
        for (var x = centerX - half; x <= centerX + half && !found; x += 2)
        {
            for (var y = centerY - half; y <= centerY + half && !found; y += 2)
            {
                found = SkiaPageComposerTests.IsStrongInk(bitmap.GetPixel(x, y));
            }
        }
        Assert.True(found, "expected medallion pixels near the foot of the cover");
    }

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
