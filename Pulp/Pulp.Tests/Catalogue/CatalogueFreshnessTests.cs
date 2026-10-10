using Pulp.Domain.Catalogue;

namespace Pulp.Tests.Catalogue;

/// <summary>
/// When a cached title is asked about again (D204): weekly for a title someone tracks, a fresh look
/// when a title page is opened — throttled, so paging back and forth cannot spend the month's
/// thousand lookups — and never otherwise. A wrong rule fails silently in both directions: too
/// eager and the allowance is gone by the third week; too lazy and a title shows homes it left.
/// </summary>
public class CatalogueFreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 20, 19, 0, 0, TimeSpan.Zero);

    private static CacheStamp FetchedAgo(TimeSpan age, bool invalidated = false) => new(Now - age, invalidated);

    [Fact]
    public void ATitleNeverFetchedIsFetched() =>
        Assert.True(CatalogueFreshness.NeedsFetch(null, tracked: false, opened: false, Now));

    [Fact]
    public void ATrackedTitleRefreshesWeekly()
    {
        Assert.False(CatalogueFreshness.NeedsFetch(FetchedAgo(TimeSpan.FromDays(6.9)), tracked: true, opened: false, Now));
        Assert.True(CatalogueFreshness.NeedsFetch(FetchedAgo(CatalogueFreshness.TrackedRefresh), tracked: true, opened: false, Now));
    }

    [Fact]
    public void AnUntrackedTitleIsPaintedFromTheCacheHoweverOld() =>
        Assert.False(CatalogueFreshness.NeedsFetch(FetchedAgo(TimeSpan.FromDays(90)), tracked: false, opened: false, Now));

    [Fact]
    public void OpeningATitleLooksAgainOnlyOnceTheThrottleHasPassed()
    {
        Assert.False(CatalogueFreshness.NeedsFetch(FetchedAgo(TimeSpan.FromMinutes(30)), tracked: false, opened: true, Now));
        Assert.True(CatalogueFreshness.NeedsFetch(FetchedAgo(CatalogueFreshness.OpenThrottle), tracked: false, opened: true, Now));
        Assert.True(CatalogueFreshness.OpenThrottle < CatalogueFreshness.TrackedRefresh);
    }

    [Fact]
    public void AChangeFromTheDailyFeedMakesTheCopyStale() =>
        Assert.True(CatalogueFreshness.NeedsFetch(FetchedAgo(TimeSpan.FromMinutes(5), invalidated: true), tracked: false, opened: false, Now));

    /// <summary>A copy stamped in the future (the phone's clock stepped back) would otherwise stay fresh until real time caught up.</summary>
    [Fact]
    public void ACopyFromTheFutureIsStale() =>
        Assert.True(CatalogueFreshness.NeedsFetch(new CacheStamp(Now.AddDays(3), false), tracked: true, opened: false, Now));

    [Fact]
    public void TheMonthOfALookupIsTheUtcMonth()
    {
        Assert.Equal("2026-05", LookupMonth.Of(new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.FromHours(-5)).AddHours(-4)));
        Assert.Equal("2026-06", LookupMonth.Of(new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void ALookupCountsIntoItsMonthAndANewMonthStartsAgain()
    {
        var tally = LookupMonth.Count(null, Now);
        tally = LookupMonth.Count(tally.Tally, Now.AddDays(1));
        Assert.Equal(new MonthTally("2026-05", 2), tally.Tally);
        Assert.Null(tally.Closed);

        var next = LookupMonth.Count(tally.Tally, new DateTimeOffset(2026, 6, 1, 0, 0, 1, TimeSpan.Zero));

        Assert.Equal(new MonthTally("2026-06", 1), next.Tally);
        Assert.Equal(new MonthTally("2026-05", 2), next.Closed);
    }

    /// <summary>D261: Diagnostics only, and not a line per lookup — that would evict the failure history the log exists for.</summary>
    [Fact]
    public void OnlyMilestonesAreWorthALine()
    {
        Assert.True(LookupMonth.IsMilestone(1));
        Assert.False(LookupMonth.IsMilestone(2));
        Assert.False(LookupMonth.IsMilestone(99));
        Assert.True(LookupMonth.IsMilestone(LookupMonth.LineEvery));
        Assert.True(LookupMonth.IsMilestone(LookupMonth.LineEvery * 7));
    }
}
