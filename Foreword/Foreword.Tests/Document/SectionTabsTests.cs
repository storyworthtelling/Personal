using Foreword.Domain.Document;
using Foreword.Infrastructure.Pdf;
using Storyworthtelling.Paper.Binder;
using Xunit;

namespace Foreword.Tests.Document;

/// <summary>
/// The Day's four tabs: their names, their links, and that the strip fits The Day's sheet. The strip's own geometry is
/// the suite's (<c>BinderStripTests</c>).
/// </summary>
public class SectionTabsTests
{
    [Fact]
    public void TheWholeStripFitsOnTheSheet()
    {
        var fits = BinderStrip.Fits(ForewordSheet.Landscape.DesignHeight, SectionTabs.FirstTabTop);

        Assert.True(fits >= Enum.GetValues<DaySection>().Length, $"The Day's sheet holds {fits} tabs.");
    }

    [Fact]
    public void EverySectionHasItsOwnDestinationName()
    {
        var names = Enum.GetValues<DaySection>().Select(SectionTabs.Destination).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public void TheTabsAreLabelledAsDrawn()
    {
        Assert.Equal(["Day", "Weather", "Outlook", "Checklist"],
            Enum.GetValues<DaySection>().Select(SectionTabs.Label));
    }
}
