using Codex.Domain.Design;
using Xunit;

namespace Codex.Tests.Design;

public class FilterMatchTests
{
    [Fact]
    public void EmptyQuery_MatchesEverything()
    {
        Assert.True(FilterMatch.Matches("Long Wave", ""));
        Assert.True(FilterMatch.Matches("Long Wave", "   "));
        Assert.True(FilterMatch.Matches(null, ""));
    }

    [Fact]
    public void NullCandidate_MatchesNothingButTheEmptyQuery()
    {
        Assert.False(FilterMatch.Matches(null, "wave"));
    }

    [Fact]
    public void MatchesASubstringAnywhere()
    {
        Assert.True(FilterMatch.Matches("Approaching Weather", "weather"));
        Assert.True(FilterMatch.Matches("Approaching Weather", "roach"));
        Assert.False(FilterMatch.Matches("Approaching Weather", "storm"));
    }

    [Fact]
    public void IgnoresCase()
    {
        Assert.True(FilterMatch.Matches("Long Wave", "LONG"));
        Assert.True(FilterMatch.Matches("LONG WAVE", "long"));
    }

    // Nobody types a diacritic to find a record, and the tag almost always has one.
    [Fact]
    public void IgnoresDiacritics()
    {
        Assert.True(FilterMatch.Matches("Béla Bartók", "bartok"));
        Assert.True(FilterMatch.Matches("Bela Bartok", "bartók"));
        Assert.True(FilterMatch.Matches("Café Tacvba", "cafe"));
        Assert.True(FilterMatch.Matches("Sigur Rós", "ros"));
    }

    [Fact]
    public void IgnoresSurroundingWhitespaceInTheQuery()
    {
        Assert.True(FilterMatch.Matches("Long Wave", "  wave  "));
    }

    // A filter runs on every keystroke over a whole library, so it must not throw
    // on the odd things tags contain.
    [Fact]
    public void HandlesEmptyAndOddCandidates()
    {
        Assert.False(FilterMatch.Matches("", "wave"));
        Assert.True(FilterMatch.Matches("—", "—"));
        Assert.True(FilterMatch.Matches("Kettle & Pine", "&"));
    }

    [Fact]
    public void MatchesAny_IsTrueWhenAnyFieldMatches()
    {
        Assert.True(FilterMatch.MatchesAny("wave", "Approaching Weather", "Kettle & Pine", "Long Wave"));
        Assert.False(FilterMatch.MatchesAny("banjo", "Approaching Weather", "Kettle & Pine", "Long Wave"));
    }

    [Fact]
    public void MatchesAny_EmptyQuery_MatchesEvenWithNoFields()
    {
        Assert.True(FilterMatch.MatchesAny(""));
    }

    [Fact]
    public void MatchesAny_SkipsNullFields()
    {
        Assert.True(FilterMatch.MatchesAny("pine", null, "Kettle & Pine"));
        Assert.False(FilterMatch.MatchesAny("pine", null, null));
    }

    // The mockups left "fits one screen" as a count, and noted the cost: a list at the
    // boundary would gain and lose its field as items came and went. A fixed threshold
    // is the predictable half of that trade, which is the property that matters — you
    // learn where the field is, rather than checking each time.
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(FilterMatch.ScreenfulRows, false)]
    [InlineData(FilterMatch.ScreenfulRows + 1, true)]
    [InlineData(400, true)]
    public void IsWarranted_TracksWhetherTheListOutrunsAScreen(int rows, bool expected)
    {
        Assert.Equal(expected, FilterMatch.IsWarranted(rows));
    }

    [Fact]
    public void ScreenfulIsAWholeListOfRowsAtTheMinimumHeight()
    {
        // A 56dp row (§5's list minimum) against a content area of a few hundred dp.
        Assert.InRange(FilterMatch.ScreenfulRows, 8, 20);
    }
}
