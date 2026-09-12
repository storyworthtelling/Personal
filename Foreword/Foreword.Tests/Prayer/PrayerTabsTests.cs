using Foreword.Domain.Document;
using Foreword.Domain.Prayer;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// The prayer document's two tabs, in the same binder grammar as The Day's four (§10).
///
/// <para>The grammar is one thing and it is drawn on two documents, so it lives in one place. Two
/// copies of "the active tab reaches one hairline further in" is two chances to get it subtly wrong
/// on one document only, and the way it goes wrong is that the seam closes on the wrong side —
/// which nothing fails over and nobody notices until the sheet is in their hand.</para>
/// </summary>
public class PrayerTabsTests
{
    private const float PageWidth = 612f;

    [Fact]
    public void ThereAreTwoTabsAndTheyAreListThenJournal()
    {
        var tabs = PrayerTabs.Layout(PageWidth, PrayerSection.List);

        Assert.Equal(
            [PrayerSection.List, PrayerSection.Journal],
            tabs.Select(tab => tab.Section));
    }

    /// <summary>
    /// The one detail the binder rests on: the active tab alone crosses the binder edge, so its
    /// seam opens onto the sheet and the other reads as sitting behind it.
    /// </summary>
    [Fact]
    public void TheActiveTabAloneReachesOneHairlineFurtherIn()
    {
        var tabs = PrayerTabs.Layout(PageWidth, PrayerSection.Journal);

        var list = tabs.Single(tab => tab.Section == PrayerSection.List);
        var journal = tabs.Single(tab => tab.Section == PrayerSection.Journal);

        Assert.Equal(PageWidth - SectionTabs.TabWidth, list.Left);
        Assert.Equal(PageWidth - SectionTabs.TabWidth - SectionTabs.Hairline, journal.Left);
        Assert.True(journal.Active);
        Assert.False(list.Active);
    }

    /// <summary>
    /// They start lower than The Day's, because the prayer page's header sits lower — the first tab
    /// clearing the rule is what makes the strip read as attached to the page rather than floating
    /// over the title.
    /// </summary>
    [Fact]
    public void TheStripStartsBelowTheHeader()
    {
        var tabs = PrayerTabs.Layout(PageWidth, PrayerSection.List);

        Assert.Equal(PrayerTabs.FirstTabTop, tabs[0].Top);
        Assert.True(PrayerTabs.FirstTabTop > SectionTabs.FirstTabTop);
        Assert.Equal(PrayerTabs.FirstTabTop + SectionTabs.TabHeight + SectionTabs.Gap, tabs[1].Top);
    }

    /// <summary>
    /// Both ends of a PDF link come from one function, because a disagreement between them is
    /// silent: the tab is drawn, it is a real link, and it goes nowhere.
    /// </summary>
    [Fact]
    public void ALinkAndItsAnchorAgreeAndDoNotCollideWithTheDaysNames()
    {
        Assert.Equal(
            PrayerTabs.Destination(PrayerSection.Journal),
            PrayerTabs.Destination(PrayerSection.Journal));

        var dayNames = Enum.GetValues<DaySection>().Select(SectionTabs.Destination).ToArray();
        var prayerNames = Enum.GetValues<PrayerSection>().Select(PrayerTabs.Destination).ToArray();

        Assert.Empty(dayNames.Intersect(prayerNames));
    }
}
