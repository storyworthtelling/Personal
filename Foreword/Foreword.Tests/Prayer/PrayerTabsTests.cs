using Foreword.Domain.Document;
using Foreword.Domain.Prayer;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>The prayer document's two tabs: list then journal, a strip that starts under its lower header, and links
/// that never collide with The Day's.</summary>
public class PrayerTabsTests
{
    [Fact]
    public void ThereAreTwoTabsAndTheyAreListThenJournal()
    {
        Assert.Equal(["List", "Journal"], Enum.GetValues<PrayerSection>().Select(PrayerTabs.Label));
    }

    [Fact]
    public void TheStripStartsBelowTheHeader()
    {
        Assert.True(PrayerTabs.FirstTabTop > SectionTabs.FirstTabTop);
    }

    [Fact]
    public void ALinkAndItsAnchorAgreeAndDoNotCollideWithTheDaysNames()
    {
        var dayNames = Enum.GetValues<DaySection>().Select(SectionTabs.Destination).ToArray();
        var prayerNames = Enum.GetValues<PrayerSection>().Select(PrayerTabs.Destination).ToArray();

        Assert.Empty(dayNames.Intersect(prayerNames));
        Assert.Equal(prayerNames.Length, prayerNames.Distinct(StringComparer.Ordinal).Count());
    }
}
