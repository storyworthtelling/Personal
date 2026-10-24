using Folio.Domain.Imaging;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using Xunit;

namespace Folio.Tests.Report;

// The first full book came out at 173 MB: 67 scene illustrations, each written into the PDF losslessly.
// The reMarkable documents a 100 MB limit, and holding a file that size in memory ran the phone out of it.
// The report's pictures are evidence, drawn a few inches wide on an e-ink page; they do not need to be lossless.
public class ReportSizeTests
{
    [Fact]
    public void ASceneIllustrationCostsFarLessThanItsOwnPng()
    {
        var picture = SampleImages.Textured();

        var pdf = new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of(),
            new ReportCover("The Salt Line", 1, 94_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null),
            [new SceneEvidence("Ch. 1 · Scene 1", "Purpose", "Verdict", picture, null)]);

        // The document is more than the picture (fonts, 100 pages of type), so the bound is on the
        // picture alone: the whole PDF must come in under what a lossless copy of it would have cost.
        Assert.True(pdf.Length < picture.Length,
            $"the PDF ({pdf.Length:N0} bytes) was no smaller than one illustration's PNG ({picture.Length:N0}); " +
            "pictures are being written losslessly again");
    }
}
