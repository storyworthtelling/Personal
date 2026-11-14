using Codex.Domain.Voice;
using Xunit;

namespace Codex.Tests.Voice;

// Efficiency audit §4 and the owner's decision (keep the strong model): every cold start of Flyleaf and every open of
// Score composed a greeting, and the launcher threw it away after 2.5 seconds. A line is now kept for its part of the
// day and reused; a compose the screen gave up waiting for still lands in the cache for the next start.
public class GreetingCacheTests
{
    private static readonly DateTime Morning = new(2026, 11, 14, 8, 30, 0);

    [Theory]
    [InlineData(5, "morning")]
    [InlineData(11, "morning")]
    [InlineData(12, "afternoon")]
    [InlineData(16, "afternoon")]
    [InlineData(17, "evening")]
    [InlineData(2, "evening")]
    public void ThePartOfTheDayFollowsTheClock(int hour, string expected) =>
        Assert.Equal(expected, GreetingCache.PartOfDay(new DateTime(2026, 11, 14, hour, 0, 0)));

    [Fact]
    public void TheSameMorningReusesItsLine()
    {
        var first = GreetingCache.Key("welcome", "opened Flyleaf", Morning);
        var later = GreetingCache.Key("welcome", "opened Flyleaf", Morning.AddHours(2));

        Assert.Equal(first, later);
    }

    [Fact]
    public void AnotherPartOfTheDayOrAnotherDayOrAnotherContextComposesAgain()
    {
        var key = GreetingCache.Key("welcome", "opened Flyleaf", Morning);

        Assert.NotEqual(key, GreetingCache.Key("welcome", "opened Flyleaf", Morning.AddHours(5)));
        Assert.NotEqual(key, GreetingCache.Key("welcome", "opened Flyleaf", Morning.AddDays(1)));
        Assert.NotEqual(key, GreetingCache.Key("welcome", "the rescan found three new albums", Morning));
        Assert.NotEqual(key, GreetingCache.Key("crate", "opened Flyleaf", Morning));
    }

    [Fact]
    public void TheKeyCarriesNoContextWords()
    {
        var key = GreetingCache.Key("welcome", "the rescan found three new albums", Morning);

        Assert.DoesNotContain("rescan", key, StringComparison.Ordinal);
    }
}
