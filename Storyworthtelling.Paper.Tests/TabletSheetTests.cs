using PdfPig = UglyToad.PdfPig;
using Storyworthtelling.Paper;

namespace Storyworthtelling.Paper.Tests;

// A document drawn in its own points (Foreword's Letter, Index's A4) placed on the tablet's own page (audit 01 §12):
// one scale, so the page is the glass and the type is the size it always looked on it, and one conversion, so a region
// named to a vision read is in the page's coordinates rather than the painter's.
public class TabletSheetTests
{
    [Fact]
    public void FitKeepsTheDrawingAndCentresItOnTheGlass()
    {
        var sheet = TabletSheet.Fit(595f, 842f);

        Assert.Equal((RemarkablePage.Width, RemarkablePage.Height), (sheet.PageWidth, sheet.PageHeight));
        Assert.Equal((595f, 842f), (sheet.DesignWidth, sheet.DesignHeight));
        Assert.Equal(594f / 842f, sheet.Scale, 4);
        Assert.Equal(0f, sheet.OffsetY, 3);
        Assert.Equal((445f - 595f * sheet.Scale) / 2f, sheet.OffsetX, 3);
    }

    [Fact]
    public void FillGrowsTheShortSideSoTheWholeGlassIsUsed()
    {
        var portrait = TabletSheet.Fill(612f, 792f);
        var landscape = TabletSheet.Fill(792f, 612f);

        Assert.Equal(445f / 612f, portrait.Scale, 4);
        Assert.Equal(612f, portrait.DesignWidth, 3);
        Assert.Equal(594f / portrait.Scale, portrait.DesignHeight, 3);
        Assert.True(portrait.DesignHeight > 792f);
        Assert.Equal((0f, 0f), (portrait.OffsetX, portrait.OffsetY));

        Assert.Equal((RemarkablePage.Height, RemarkablePage.Width), (landscape.PageWidth, landscape.PageHeight));
        Assert.Equal(612f, landscape.DesignHeight, 3);
        Assert.True(landscape.DesignWidth > 792f);
    }

    [Fact]
    public void ARegionIsGivenInThePagesOwnPoints()
    {
        var sheet = TabletSheet.Fit(595f, 842f);

        var page = sheet.ToPage(new SheetRect(100f, 200f, 50f, 30f));

        Assert.Equal(sheet.OffsetX + 100f * sheet.Scale, page.Left, 3);
        Assert.Equal(200f * sheet.Scale, page.Top, 3);
        Assert.Equal(50f * sheet.Scale, page.Width, 3);
        Assert.Equal(30f * sheet.Scale, page.Height, 3);
    }

    [Fact]
    public void ThePdfIsTheTabletsPageAndTheDrawingLandsWhereToPageSays()
    {
        var sheet = TabletSheet.Fit(595f, 842f);
        using var sink = new VectorPdfSink();
        var canvas = sink.BeginPage(sheet);
        using (var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black })
        using (var font = new SkiaSharp.SKFont(EmbeddedTypefaces.SansRegular, 20))
        {
            canvas.DrawText("Mark", 100f, 200f, font, paint);
        }
        sink.EndPage();

        using var pdf = PdfPig.PdfDocument.Open(sink.Close());
        var page = pdf.GetPage(1);
        Assert.Equal(445d, page.Width, 1);
        Assert.Equal(594d, page.Height, 1);
        var first = page.Letters.First();
        // PdfPig measures from the bottom-left; the baseline was drawn at y = 200 in the painter's points.
        Assert.Equal(sheet.OffsetX + 100f * sheet.Scale, first.StartBaseLine.X, 0);
        Assert.Equal(594f - 200f * sheet.Scale, first.StartBaseLine.Y, 0);
    }
}
