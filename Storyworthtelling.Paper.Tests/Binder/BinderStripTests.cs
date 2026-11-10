using Storyworthtelling.Paper.Binder;

namespace Storyworthtelling.Paper.Tests.Binder;

// The binder grammar every tablet document shares (DESIGN.md §10), moved out of Foreword so Index, Constitution and
// Folio draw the same strip: tabs stacked down the right edge, the active one alone crossing the binder edge.
public class BinderStripTests
{
    private const float PageWidth = 612f;

    [Fact]
    public void TheTabsStackDownTheEdgeWithAGapBetweenThem()
    {
        var tabs = BinderStrip.Layout(PageWidth, count: 4, active: 0, firstTabTop: 96f);

        Assert.Equal(4, tabs.Count);
        Assert.Equal(96f, tabs[0].Top, 3);
        for (var i = 1; i < tabs.Count; i++)
        {
            Assert.Equal(tabs[i - 1].Top + BinderStrip.TabHeight + BinderStrip.Gap, tabs[i].Top, 3);
        }
    }

    [Fact]
    public void TheActiveTabAloneReachesOneHairlineFurtherIn()
    {
        var tabs = BinderStrip.Layout(PageWidth, count: 3, active: 1, firstTabTop: 96f);

        Assert.Equal([false, true, false], tabs.Select(t => t.Active));
        Assert.Equal(PageWidth - BinderStrip.TabWidth, tabs[0].Left, 3);
        Assert.Equal(PageWidth - BinderStrip.TabWidth - BinderStrip.Hairline, tabs[1].Left, 3);
        Assert.Equal(BinderStrip.TabWidth + BinderStrip.Hairline, tabs[1].Width, 3);
        Assert.All(tabs, tab => Assert.True(tab.Left + tab.Width <= PageWidth));
    }

    [Fact]
    public void AOnePageDocumentStillCarriesItsOwnTab()
    {
        // One tab per page, by the owner's decision: a single sheet shows where you are, as every Foreword page does.
        var tab = Assert.Single(BinderStrip.Layout(PageWidth, count: 1, active: 0, firstTabTop: 96f));

        Assert.True(tab.Active);
    }

    [Fact]
    public void TheContentStopsClearOfTheBinderEdge()
    {
        Assert.True(BinderStrip.Gutter > BinderStrip.TabWidth + BinderStrip.Hairline);
        Assert.Equal(PageWidth - BinderStrip.Gutter, BinderStrip.ContentRight(PageWidth), 3);
    }

    [Fact]
    public void HowManyTabsFitIsWhatTheSheetHolds()
    {
        var fit = BinderStrip.Fits(pageHeight: 612f, firstTabTop: 96f);

        var last = BinderStrip.Layout(PageWidth, fit, 0, 96f)[^1];
        Assert.True(last.Top + last.Height <= 612f - SheetFrame.Margin);
        Assert.True(96f + fit * (BinderStrip.TabHeight + BinderStrip.Gap) + BinderStrip.TabHeight > 612f - SheetFrame.Margin);
    }
}
