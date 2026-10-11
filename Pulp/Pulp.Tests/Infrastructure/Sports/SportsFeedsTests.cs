using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;
using Pulp.Infrastructure.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Infrastructure.Sports;

/// <summary>
/// The refresh pass behind the Sports tab: cache first, a fetch only when <see cref="SportsFreshness"/> says
/// so, a failed fetch leaving the cached copy standing. No network here — the source is a fake.
/// </summary>
public sealed class SportsFeedsTests : IDisposable
{
    private static readonly ViewerId Robin = new("robin-example");

    private readonly string directory = Path.Combine(Path.GetTempPath(), "pulp-feeds-" + Guid.NewGuid().ToString("N"));
    private readonly FakeSource source = new();
    private readonly List<string> warnings = [];
    private DateTimeOffset clock = Now;

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private SportsCache Cache() => new(directory);

    private SportsFeeds Feeds() => new(
        source,
        new SportsSourceTable(
            [new SportsSourceInfo("one", "One", "t")],
            [new SportsLeagueEntry(Coastal, "Coastal", "one"), new SportsLeagueEntry(Southern, "Southern", "one")]),
        Cache(),
        (message, exception) => warnings.Add(message),
        () => clock);

    private static SportsFollow Follows(TeamId team) => new(Robin, team);

    private sealed class FakeSource : ISportsSource
    {
        public List<string> Calls { get; } = [];

        public List<SportsWindow> Windows { get; } = [];

        public bool FailSchedule { get; set; }

        public bool FailStandings { get; set; }

        public TaskCompletionSource? Gate { get; set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<LeagueId> Leagues { get; } = [Coastal, Southern];

        public async Task<LeagueSchedule> GetScheduleAsync(LeagueId league, SportsWindow window, CancellationToken cancellationToken)
        {
            Calls.Add("schedule:" + league);
            Started.TrySetResult();
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            Windows.Add(window);
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSchedule)
            {
                throw new CatalogueLookupException(LookupSource.Espn, LookupFailure.Unavailable, 500, "the invented source is down");
            }

            var feed = league == Coastal
                ? CoastalFeed()
                : new LeagueFeed(
                    new League(Southern, "Southern Rules League", "badge://srl", SeasonPhase.RegularSeason, new DateOnly(2026, 9, 1), null),
                    [Otters], [], [], [], null);
            return new LeagueSchedule(feed.League, feed.Teams, feed.Schedule, feed.Championship);
        }

        public Task<IReadOnlyList<Team>> GetStandingsOrderAsync(LeagueId league, CancellationToken cancellationToken)
        {
            Calls.Add("standings:" + league);
            if (FailStandings)
            {
                throw new CatalogueLookupException(LookupSource.Espn, LookupFailure.Malformed, 200, "the invented standings are not the documented shape");
            }

            return Task.FromResult<IReadOnlyList<Team>>(league == Coastal ? [Herons, Gulls, Foxes, Otters] : [Otters]);
        }
    }

    [Fact]
    public async Task AFollowedTeamsLeagueIsFetchedOnceAndCachedThenServedWithoutAskingAgain()
    {
        Cache().Store(CoastalFeed(), clock.AddHours(-30));
        var feeds = Feeds();

        var first = await feeds.FollowedAsync([Follows(Gulls.Id)], refresh: true, CancellationToken.None);
        Assert.Equal(["schedule:cfl-invented", "standings:cfl-invented"], source.Calls);
        Assert.Equal([Coastal], first.Select(feed => feed.League.Id));

        source.Calls.Clear();
        await feeds.FollowedAsync([Follows(Gulls.Id)], refresh: true, CancellationToken.None);
        Assert.Empty(source.Calls);
        Assert.Equal(clock, Cache().Read(Coastal).Entry!.FetchedAt);
    }

    [Fact]
    public async Task TheScheduleIsAskedForTheRecordingsAndTwoWeeksAheadFromTheClocksDay()
    {
        await Feeds().FollowedAsync([Follows(Gulls.Id)], refresh: true, CancellationToken.None);

        // Nothing was cached, so a one-day look finds the league first; the full fetch is the last window asked.
        Assert.Equal(SportsWindow.Around(new DateOnly(2026, 1, 20)), source.Windows.Last());
        Assert.Equal(new SportsWindow(new DateOnly(2026, 1, 20), 0, 0), source.Windows.First());
    }

    [Fact]
    public async Task ReadingWithoutRefreshNeverTouchesTheSource()
    {
        Cache().Store(CoastalFeed(), clock.AddDays(-9));

        var feeds = await Feeds().FollowedAsync([Follows(Gulls.Id)], refresh: false, CancellationToken.None);

        Assert.Empty(source.Calls);
        Assert.Equal([Coastal], feeds.Select(feed => feed.League.Id));
    }

    [Fact]
    public async Task ACacheOnlyReadDoesNotWaitBehindARefreshInFlight()
    {
        Cache().Store(CoastalFeed(), clock.AddDays(-9));
        source.Gate = new TaskCompletionSource();
        var feeds = Feeds();

        var refreshing = feeds.FollowedAsync([Follows(Gulls.Id)], refresh: true, CancellationToken.None);
        await source.Started.Task;

        var painted = feeds.FollowedAsync([Follows(Gulls.Id)], refresh: false, CancellationToken.None);
        var finished = await Task.WhenAny(painted, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(painted, finished);
        Assert.Equal([Coastal], (await painted).Select(feed => feed.League.Id));
        source.Gate.SetResult();
        await refreshing;
    }

    [Fact]
    public async Task AnUnreadableCacheEntryMeansNoCacheAndTheLeagueIsFetchedAgain()
    {
        Cache().Store(CoastalFeed(), clock);
        File.WriteAllText(Directory.GetFiles(directory).Single(), "{ \"format\": 1, \"league\": { \"id\": null } }");

        var feeds = await Feeds().FollowedAsync([Follows(Gulls.Id)], refresh: true, CancellationToken.None);

        Assert.Equal([Coastal], feeds.Select(feed => feed.League.Id));
        Assert.Contains(warnings, message => message.Contains("could not be read"));
        Assert.Contains("schedule:cfl-invented", source.Calls);
    }

    [Fact]
    public async Task AFailedStoreIsLoggedAndTheFreshFeedStillComesBack()
    {
        // The cache directory's parent is a file, so no store can succeed.
        var blocker = Path.Combine(Path.GetTempPath(), "pulp-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "not a directory");
        try
        {
            var feeds = new SportsFeeds(
                source,
                new SportsSourceTable(
                    [new SportsSourceInfo("one", "One", "t")],
                    [new SportsLeagueEntry(Coastal, "Coastal", "one"), new SportsLeagueEntry(Southern, "Southern", "one")]),
                new SportsCache(Path.Combine(blocker, "inside")),
                (message, exception) => warnings.Add(message),
                () => clock);

            var found = await feeds.FollowedAsync([Follows(Gulls.Id)], refresh: true, CancellationToken.None);
            var browsed = await feeds.BrowseAsync(CancellationToken.None);

            Assert.Equal([Coastal], found.Select(feed => feed.League.Id));
            Assert.NotEmpty(browsed);
            Assert.Contains(warnings, message => message.Contains("could not be saved"));
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public async Task NoFollowsMeansNothingFetchedAndNothingReturned()
    {
        var feeds = await Feeds().FollowedAsync([], refresh: true, CancellationToken.None);

        Assert.Empty(feeds);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task ATeamACachedLeagueHasNeedsNoDiscoveryAndAnUnlocatedOneIsLookedForInTheUncachedLeaguesOnly()
    {
        Cache().Store(CoastalFeed(), clock);
        var feeds = Feeds();

        await feeds.FollowedAsync([Follows(Otters.Id)], refresh: true, CancellationToken.None);
        Assert.Empty(source.Calls);

        var found = await feeds.FollowedAsync([Follows(new TeamId("nobody-has-this-example"))], refresh: true, CancellationToken.None);
        Assert.Empty(found);
        Assert.Equal(["schedule:srl-invented"], source.Calls);
    }

    [Fact]
    public async Task ADiscoveredLeagueIsStoredStaleSoTheSamePassFetchesItInFull()
    {
        var feeds = Feeds();
        Cache().Store(CoastalFeed(), clock);

        // The Otters play in both leagues, so cached Coastal already locates them: nothing to discover.
        await feeds.FollowedAsync([Follows(Otters.Id)], refresh: true, CancellationToken.None);
        Assert.DoesNotContain("schedule:srl-invented", source.Calls);

        // A fresh install: nothing cached, so both leagues are discovered, found to hold the team, and fetched in full.
        Directory.Delete(directory, recursive: true);
        source.Calls.Clear();
        source.Windows.Clear();
        var fresh = await Feeds().FollowedAsync([Follows(Otters.Id)], refresh: true, CancellationToken.None);

        Assert.Equal(2, fresh.Count);
        var discovery = source.Windows.Where(window => window.DaysBack == 0 && window.DaysAhead == 0).ToList();
        Assert.Equal(2, discovery.Count);
        Assert.Equal(2, source.Windows.Count(window => window.DaysBack == SportsWindow.RecordingDays));
    }

    [Fact]
    public async Task AFailedFetchKeepsTheCachedCopyAndSaysSo()
    {
        Cache().Store(CoastalFeed(), clock.AddDays(-2));
        source.FailSchedule = true;

        var feeds = await Feeds().FollowedAsync([Follows(Gulls.Id)], refresh: true, CancellationToken.None);

        Assert.Equal([Coastal], feeds.Select(feed => feed.League.Id));
        Assert.Equal(clock.AddDays(-2), Cache().Read(Coastal).Entry!.FetchedAt);
        Assert.Contains(warnings, message => message.Contains("cfl-invented") && message.Contains("Unavailable"));
    }

    [Fact]
    public async Task AFailedFetchWithNothingCachedLeavesTheLeagueOut()
    {
        source.FailSchedule = true;

        var feeds = await Feeds().FollowedAsync([Follows(Gulls.Id)], refresh: true, CancellationToken.None);

        Assert.Empty(feeds);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public async Task StandingsThatFailKeepTheLastRankOrderButTheNewScheduleStands()
    {
        var old = CoastalFeed() with { Standings = [Otters, Foxes] };
        Cache().Store(old, clock.AddDays(-2));
        source.FailStandings = true;

        var feed = Assert.Single(await Feeds().FollowedAsync([Follows(Gulls.Id)], refresh: true, CancellationToken.None));

        Assert.Equal([Otters.Id, Foxes.Id], feed.Standings.Select(team => team.Id));
        Assert.Equal(clock, Cache().Read(Coastal).Entry!.FetchedAt);
        Assert.Contains(warnings, message => message.Contains("standings"));
    }

    [Fact]
    public async Task TheCallersCancellationPropagatesAndIsNotLogged()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Feeds().FollowedAsync([Follows(Gulls.Id)], refresh: true, cancellation.Token));
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task TheTeamPickerListsEveryLeagueInTheTableFromTheCacheOrADiscoveryFetch()
    {
        Cache().Store(CoastalFeed(), clock);

        var leagues = await Feeds().BrowseAsync(CancellationToken.None);

        Assert.Equal([Coastal, Southern], leagues.Select(league => league.League.Id));
        Assert.Equal(["schedule:srl-invented"], source.Calls);
        Assert.NotEmpty(leagues[0].Teams);
    }
}
