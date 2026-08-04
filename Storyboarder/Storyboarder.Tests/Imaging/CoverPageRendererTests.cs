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
    public void VolumeCover_WithArt_FillsPageWithArtwork_NotPaper()
    {
        // A supplied illustration fills the page and the title is set over a dark scrim, rather than the
        // plain grained-paper cover.
        using var artSurface = SKSurface.Create(new SKImageInfo(400, 600));
        artSurface.Canvas.Clear(new SKColor(0x2E, 0x8B, 0x57)); // sea green, unmistakable vs paper/ink
        using var artImg = artSurface.Snapshot();
        using var artData = artImg.Encode(SKEncodedImageFormat.Png, 100);

        var cover = renderer.VolumeCover("The Lighthouse Keeper", 2, "The Harbour Debt", artData.ToArray());

        using var bitmap = SKBitmap.Decode(cover)!;
        Assert.Equal(DesignTokens.Comic.PageWidth, bitmap.Width);
        Assert.Equal(DesignTokens.Comic.PageHeight, bitmap.Height);

        // The artwork covers the page: the clear centre band is the green art.
        var mid = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.True(mid.Green > mid.Red && mid.Green > mid.Blue, $"expected green artwork at centre, got {mid}");

        // Not the paper cover: the top-left corner is under the dark scrim, nowhere near paper.
        var paper = SKColor.Parse(DesignTokens.Paper);
        var corner = bitmap.GetPixel(5, 5);
        Assert.True(Math.Abs(corner.Red - paper.Red) > 40, $"expected a dark scrim over the art, got {corner}");
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
