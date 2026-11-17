using System.Globalization;
using Storyworthtelling.Time;

namespace Storyworthtelling.Time.Tests;

/// <summary>
/// The suite's one way to say how many, how big and how long (audit W11.5, DESIGN.md §14): a count agrees with its
/// noun, a number is grouped the same on every phone whatever its language, a size says KB or MB, and a duration is
/// "42 min".
/// </summary>
public class QuantitiesTests
{
    [Theory]
    [InlineData(0, "0 tracks")]
    [InlineData(1, "1 track")]
    [InlineData(2, "2 tracks")]
    [InlineData(12500, "12,500 tracks")]
    public void ACountAgreesWithItsNoun(int count, string expected) =>
        Assert.Equal(expected, Quantities.Count(count, "track"));

    [Fact]
    public void AnIrregularNounNamesItsPlural()
    {
        Assert.Equal("1 story", Quantities.Count(1, "story", "stories"));
        Assert.Equal("3 stories", Quantities.Count(3, "story", "stories"));
    }

    [Theory]
    [InlineData(1, "once")]
    [InlineData(2, "twice")]
    [InlineData(5, "5 times")]
    public void TimesIsSaidInWordsWhereEnglishDoes(int count, string expected) =>
        Assert.Equal(expected, Quantities.Times(count));

    [Fact]
    public void ANumberIsGroupedTheSameWhateverThePhonesLanguage()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("80,000", Quantities.Number(80000));
            Assert.Equal("80,000 words", Quantities.Count(80000, "word"));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(900, "900 bytes")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "2 KB")]
    [InlineData(3_565_158, "3.4 MB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5.0 GB")]
    public void ASizeSaysItsUnit(long bytes, string expected) =>
        Assert.Equal(expected, Quantities.Size(bytes));

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(42, "42 min")]
    [InlineData(60, "1 hr")]
    [InlineData(80, "1 hr 20 min")]
    public void ADurationIsMinutesThenHours(int minutes, string expected) =>
        Assert.Equal(expected, Quantities.Minutes(minutes));

    [Fact]
    public void ADurationRoundsToTheMinute() =>
        Assert.Equal("3 min", Quantities.Duration(TimeSpan.FromSeconds(170)));

    [Theory]
    [InlineData(60, "60 Hz")]
    [InlineData(1000, "1 kHz")]
    [InlineData(3600, "3.6 kHz")]
    [InlineData(14000, "14 kHz")]
    public void AFrequencyKeepsItsTenths(int hertz, string expected) =>
        Assert.Equal(expected, Quantities.Frequency(hertz));
}
