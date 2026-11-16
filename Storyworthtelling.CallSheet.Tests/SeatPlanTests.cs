using Storyworthtelling.CallSheet;

namespace Storyworthtelling.CallSheet.Tests;

/// <summary>
/// The one rule for when a seat is composed (audit W10.3): at most once per seat, facts and day; kept across a
/// restart; and an attempt that came back empty is tried again after a short wait rather than the next day.
/// </summary>
public class SeatPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 3, 9);
    private static readonly VoiceLine Hidden = CallSheet.For("Index", "accounts");
    private static readonly VoiceLine Neutral = CallSheet.For("Pulp", "empty-playlists");

    [Fact]
    public void NoVoiceMeansTheFallback()
    {
        Assert.Equal(SeatAction.Fallback, SeatPlan.Decide(Hidden, voiceReady: false, "h", hasFacts: true, null, Now, Today));
    }

    [Fact]
    public void AHiddenSeatWithNoFactsIsNotAsked()
    {
        Assert.Equal(SeatAction.Fallback, SeatPlan.Decide(Hidden, true, "h", hasFacts: false, null, Now, Today));
    }

    [Fact]
    public void AnEmptyPageIsAskedEvenWithoutFacts()
    {
        Assert.Equal(SeatAction.Compose, SeatPlan.Decide(Neutral, true, "h", hasFacts: false, null, Now, Today));
    }

    [Fact]
    public void TheSameFactsTheSameDayReuseTheLine()
    {
        var kept = SeatPlan.Remember("h", Today, "A line.", Now.AddHours(-3));

        Assert.Equal(SeatAction.Reuse, SeatPlan.Decide(Hidden, true, "h", true, kept, Now, Today));
    }

    [Fact]
    public void NewFactsOrANewDayComposeAgain()
    {
        var kept = SeatPlan.Remember("h", Today, "A line.", Now.AddHours(-3));

        Assert.Equal(SeatAction.Compose, SeatPlan.Decide(Hidden, true, "other", true, kept, Now, Today));
        Assert.Equal(SeatAction.Compose, SeatPlan.Decide(Hidden, true, "h", true, kept, Now.AddDays(1), Today.AddDays(1)));
    }

    [Fact]
    public void AnEmptyAttemptWaitsOnlyAShortWhile()
    {
        var empty = SeatPlan.Remember("h", Today, null, Now);

        Assert.Equal(SeatAction.Fallback, SeatPlan.Decide(Hidden, true, "h", true, empty, Now.AddMinutes(5), Today));
        Assert.Equal(SeatAction.Compose,
            SeatPlan.Decide(Hidden, true, "h", true, empty, Now + SeatPlan.RetryAfter, Today));
    }

    [Fact]
    public void TheFactsHashIsStableAndNamesItsSeat()
    {
        var one = SeatPlan.Hash(Hidden, ["Debt is down about one percent."]);

        Assert.Equal(one, SeatPlan.Hash(Hidden, ["Debt is down about one percent."]));
        Assert.NotEqual(one, SeatPlan.Hash(CallSheet.For("Index", "snowball"), ["Debt is down about one percent."]));
        Assert.Matches("^[0-9a-f]{16}$", one);
    }
}

/// <summary>Which day a line belongs to: an evening that runs past midnight is the evening of the day it began.</summary>
public class SeatDayTests
{
    [Theory]
    [InlineData(9, 9, 0, 9)]
    [InlineData(9, 23, 30, 9)]
    [InlineData(10, 0, 30, 9)]
    [InlineData(10, 4, 59, 9)]
    [InlineData(10, 5, 0, 10)]
    public void TheDayTurnsAtFive(int day, int hour, int minute, int expectedDay)
    {
        Assert.Equal(new DateOnly(2026, 3, expectedDay), SeatPlan.Today(new DateTime(2026, 3, day, hour, minute, 0)));
    }
}
