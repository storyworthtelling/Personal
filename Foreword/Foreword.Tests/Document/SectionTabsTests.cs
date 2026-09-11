using Foreword.Domain.Document;
using Xunit;

namespace Foreword.Tests.Document;

/// <summary>
/// The binder tabs down the right edge of every page of The Day (§10, D1-D4).
///
/// <para>They are the reason the four documents became one. Four separate files on a tablet are
/// four things to find in a list; four tabbed pages are one thing to open and three taps to move
/// around in — and the reMarkable honours a PDF link annotation, activated by finger rather than
/// pen, so the tabs actually work rather than merely looking like they should.</para>
/// </summary>
public class SectionTabsTests
{
    private const float PageWidth = 792f;
    private const float PageHeight = 612f;

    [Fact]
    public void EveryPageCarriesAllFourTabsInOrder()
    {
        var tabs = SectionTabs.Layout(PageWidth, DaySection.Day);

        Assert.Equal([DaySection.Day, DaySection.Weather, DaySection.Outlook, DaySection.Checklist],
            tabs.Select(t => t.Section));
    }

    [Fact]
    public void TheTabsStackDownTheEdgeWithAGapBetweenThem()
    {
        var tabs = SectionTabs.Layout(PageWidth, DaySection.Day);

        for (var i = 1; i < tabs.Count; i++)
        {
            Assert.Equal(tabs[i - 1].Top + SectionTabs.TabHeight + SectionTabs.Gap, tabs[i].Top, 3);
        }

        Assert.Equal(SectionTabs.FirstTabTop, tabs[0].Top, 3);
    }

    /// <summary>
    /// Every tab has to fit on the sheet. The strip is four tabs and three gaps from a fixed top,
    /// and the page is only 612 points tall — a fifth section, or a taller tab, would run the last
    /// one off the bottom edge where it would be drawn, linked, and unreachable.
    /// </summary>
    [Fact]
    public void TheWholeStripFitsOnTheSheet()
    {
        var tabs = SectionTabs.Layout(PageWidth, DaySection.Day);

        Assert.True(tabs[^1].Top + SectionTabs.TabHeight < PageHeight,
            $"The last tab ends at {tabs[^1].Top + SectionTabs.TabHeight} on a {PageHeight}pt page.");
    }

    /// <summary>
    /// The active tab alone touches the page: it reaches one hairline further in, covering the
    /// binder edge, so its seam opens onto the sheet. That is the whole visual grammar of a binder —
    /// the open divider is part of the page you are looking at and the others are behind it.
    /// </summary>
    [Fact]
    public void TheActiveTabReachesOneHairlineFurtherIn()
    {
        var tabs = SectionTabs.Layout(PageWidth, DaySection.Outlook);

        var active = tabs.Single(t => t.Active);
        var inactive = tabs.First(t => !t.Active);

        Assert.Equal(DaySection.Outlook, active.Section);
        Assert.Equal(inactive.Left - SectionTabs.Hairline, active.Left, 3);
        Assert.Equal(inactive.Width + SectionTabs.Hairline, active.Width, 3);
    }

    [Fact]
    public void ExactlyOneTabIsActive()
    {
        foreach (var section in Enum.GetValues<DaySection>())
        {
            Assert.Single(SectionTabs.Layout(PageWidth, section), t => t.Active);
        }
    }

    [Fact]
    public void TheTabsSitInsideTheRightEdge()
    {
        foreach (var tab in SectionTabs.Layout(PageWidth, DaySection.Day))
        {
            Assert.True(tab.Left + tab.Width <= PageWidth,
                $"{tab.Section} runs to {tab.Left + tab.Width} on a {PageWidth}pt page.");
        }
    }

    /// <summary>
    /// Destination names are what one page's link and another page's anchor have to agree on, and a
    /// disagreement is silent: the tab is drawn, it is a real link, and it goes nowhere. So both
    /// ends come from one function.
    /// </summary>
    [Fact]
    public void EverySectionHasItsOwnDestinationName()
    {
        var names = Enum.GetValues<DaySection>().Select(SectionTabs.Destination).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    /// <summary>The words on the tabs are the words in the mockup.</summary>
    [Fact]
    public void TheTabsAreLabelledAsDrawn()
    {
        Assert.Equal(["Day", "Weather", "Outlook", "Checklist"],
            Enum.GetValues<DaySection>().Select(SectionTabs.Label));
    }
}
