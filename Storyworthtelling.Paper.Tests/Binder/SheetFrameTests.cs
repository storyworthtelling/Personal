using SkiaSharp;
using Storyworthtelling.Paper.Binder;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;

namespace Storyworthtelling.Paper.Tests.Binder;

// The frame every tablet document wears (Foreword's: a Serif title, the app's name in the owner's hand in its identity
// colour, the identity rule under them, the binder tabs as real links, and a folio at the foot), drawn once here.
public class SheetFrameTests
{
    // An invented app signed in Foreword's colours, so the test is about the frame rather than any one app.
    private static readonly SheetSignature Index = new("Index", AppIdentityTokens.ForewordSunrise, AppIdentityTokens.ForewordGround, AppIdentityTokens.ForewordDeep);

    private static byte[] TwoPages(Action<SKCanvas, int>? extra = null)
    {
        var sheet = TabletSheet.Fill(612f, 792f);
        var tabs = new[]
        {
            new BinderTab("Budget", "index-budget", Index.Cloth, Index.Deep),
            new BinderTab("Follow-up", "index-follow-up", Index.Cloth, Index.Deep),
        };
        using var sink = new VectorPdfSink();
        for (var page = 0; page < 2; page++)
        {
            var canvas = sink.BeginPage(sheet);
            SheetFrame.Clear(canvas);
            var below = SheetFrame.Header(canvas, sheet, "October 2026", Index);
            Assert.True(below > SheetFrame.Margin + SheetFrame.HeaderHeight);
            SheetFrame.Binder(canvas, sheet, tabs, active: page, firstTabTop: SheetFrame.FirstTabTop);
            SheetFrame.Folio(canvas, sheet, "Index · October 2026", page + 1);
            extra?.Invoke(canvas, page);
            sink.EndPage();
        }
        return sink.Close();
    }

    [Fact]
    public void TheHeaderCarriesTheTitleAndTheAppsName()
    {
        using var pdf = PdfDocument.Open(TwoPages());

        var text = pdf.GetPage(1).Text;
        Assert.Contains("October 2026", text);
        Assert.Contains("Index", text);
    }

    [Fact]
    public void EveryPageCarriesEveryTabAsALinkAndAnchorsItself()
    {
        var bytes = TwoPages();
        using var pdf = PdfDocument.Open(bytes);

        foreach (var page in pdf.GetPages())
        {
            Assert.Equal(2, page.GetAnnotations().Count(a => a.Type == AnnotationType.Link));
            Assert.Contains("Budget", page.Text);
            Assert.Contains("Follow-up", page.Text);
        }
        var raw = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.Contains("/Dest /index-budget", raw);
        Assert.Contains("/Dest /index-follow-up", raw);
    }

    [Fact]
    public void TheFolioNamesThePageAndItsNumber()
    {
        using var pdf = PdfDocument.Open(TwoPages());

        Assert.Contains("2", pdf.GetPage(2).GetWords().Select(w => w.Text));
        Assert.Contains("Index", pdf.GetPage(2).Text);
    }

    [Fact]
    public void EveryPageCarriesTheMakersMarkInItsFolio()
    {
        // The mark is drawn, not a picture: an oval and the M's strokes, at the head of the folio line on every page.
        using var pdf = PdfDocument.Open(TwoPages());
        var sheet = TabletSheet.Fill(612f, 792f);

        foreach (var page in pdf.GetPages())
        {
            Assert.Empty(page.GetImages());
            // Under the content's bottom edge, at the left margin, the mark's oval: about 14 board points across.
            var markLeft = SheetFrame.Margin * sheet.Scale;
            var contentBottom = (float)page.Height - SheetFrame.ContentBottom(sheet) * sheet.Scale;
            Assert.Contains(page.Paths, path => path.GetBoundingRectangle() is { } r
                && Math.Abs(r.Left - markLeft) < 3 && r.Top < contentBottom && r.Bottom > 0 && r.Width < 12 && r.Width > 4);
        }
    }

    [Fact]
    public void ACoverSaysWhoSetThePaperUnderTheMark()
    {
        var sheet = TabletSheet.Fill(612f, 792f);
        using var sink = new VectorPdfSink();
        var canvas = sink.BeginPage(sheet);
        SheetFrame.Clear(canvas);
        var top = SheetFrame.Colophon(canvas, sheet, Index, sheet.DesignHeight - SheetFrame.Margin, 504f);
        sink.EndPage();

        using var pdf = PdfDocument.Open(sink.Close());
        Assert.Contains("SET AND DELIVERED BY INDEX", pdf.GetPage(1).Text.Replace("  ", " "));
        Assert.True(top < sheet.DesignHeight - SheetFrame.Margin - SheetFrame.ColophonMarkSize);
    }

    [Fact]
    public void TheContentAreaStopsShortOfTheTabsAndTheFolio()
    {
        var sheet = TabletSheet.Fill(612f, 792f);

        var body = SheetFrame.Body(sheet, top: 100f);

        Assert.Equal(SheetFrame.Margin, body.Left, 3);
        Assert.Equal(BinderStrip.ContentRight(sheet.DesignWidth), body.Left + body.Width, 3);
        Assert.True(body.Top + body.Height < SheetFrame.FolioTop(sheet));
    }
}
