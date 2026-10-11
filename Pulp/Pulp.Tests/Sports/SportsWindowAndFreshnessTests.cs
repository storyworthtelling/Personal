using Pulp.Domain.Sports;

namespace Pulp.Tests.Sports;

public class SportsWindowAndFreshnessTests
{
    private static readonly DateOnly Today = new(2026, 1, 20);

    [Fact]
    public void TheDefaultWindowReachesBackToTheRecordingsAndTwoWeeksAhead()
    {
        var window = SportsWindow.Around(Today);

        Assert.Equal(new DateOnly(2026, 1, 6), window.From);
        Assert.Equal(new DateOnly(2026, 2, 3), window.To);
        Assert.Equal(29, window.Days().Count());
        Assert.Equal(window.From, window.Days().First());
        Assert.Equal(window.To, window.Days().Last());
    }

    [Fact]
    public void ANegativeReachIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SportsWindow(Today, -1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SportsWindow(Today, 3, -1));
    }

    [Theory]
    [InlineData(SeasonPhase.RegularSeason, 5, false)]
    [InlineData(SeasonPhase.RegularSeason, 7, true)]
    [InlineData(SeasonPhase.Postseason, 7, true)]
    [InlineData(SeasonPhase.Preseason, 7, false)]
    [InlineData(SeasonPhase.Preseason, 13, true)]
    [InlineData(SeasonPhase.OffSeason, 23, false)]
    [InlineData(SeasonPhase.OffSeason, 25, true)]
    public void HowOftenALeagueIsAskedAgainFollowsItsSeason(SeasonPhase phase, int hoursOld, bool due)
    {
        var now = new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(due, SportsFreshness.NeedsFetch(now.AddHours(-hoursOld), phase, now));
    }

    [Fact]
    public void NothingCachedIsDue()
    {
        Assert.True(SportsFreshness.NeedsFetch(null, SeasonPhase.OffSeason, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void AStampFromTheFutureIsDueBecauseTheClockSteppedBack()
    {
        var now = new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero);

        Assert.True(SportsFreshness.NeedsFetch(now.AddHours(2), SeasonPhase.OffSeason, now));
    }
}
