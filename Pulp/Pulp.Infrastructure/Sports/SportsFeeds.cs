using Pulp.Domain.Core;
using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Infrastructure.Sports;

/// <summary>A league and its teams: what the "add a team" picker lists (D67). No schedule, standings or result.</summary>
public sealed record LeagueTeams(League League, IReadOnlyList<Team> Teams);

/// <summary>
/// The refresh pass behind the Sports tab and the Calendar's games: cache first, a fetch only when
/// <see cref="SportsFreshness"/> says a league is due, and a failed fetch leaves the cached copy standing
/// (logged, never voiced). A team id alone does not name its league (D185), so a followed team no cached
/// feed lists sends the pass looking in the leagues not yet cached — a cheap one-day fetch stored as
/// already stale, so the same pass then fetches the league in full. Everything that comes back is a
/// <see cref="LeagueFeed"/>, which holds no result; what a screen may <i>see</i> of it is decided by
/// <see cref="SportsPage"/> and the spoiler hold, not here.
/// </summary>
public sealed class SportsFeeds
{
    private readonly ISportsSource source;
    private readonly SportsSourceTable table;
    private readonly SportsCache cache;
    private readonly Action<string, Exception?> warn;
    private readonly Func<DateTimeOffset> clock;
    private readonly SemaphoreSlim gate = new(1, 1);

    public SportsFeeds(ISportsSource source, SportsSourceTable table, SportsCache cache, Action<string, Exception?> warn, Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(warn);
        ArgumentNullException.ThrowIfNull(clock);
        this.source = source;
        this.table = table;
        this.cache = cache;
        this.warn = warn;
        this.clock = clock;
    }

    /// <summary>
    /// The feeds of every league a team in <paramref name="follows"/> plays in — every adult's, since the
    /// Calendar is built for the household. With <paramref name="refresh"/> false nothing is sent: what the
    /// cache holds is what comes back, for an instant first paint.
    /// </summary>
    public async Task<IReadOnlyList<LeagueFeed>> FollowedAsync(IReadOnlyCollection<SportsFollow> follows, bool refresh, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(follows);
        if (follows.Count == 0)
        {
            return [];
        }

        // A cache-only read (a peek, the first paint) sends nothing, so it never waits behind a refresh holding the gate.
        if (!refresh)
        {
            return await ReadFollowedAsync(follows, refresh: false, cancellationToken);
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadFollowedAsync(follows, refresh: true, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<LeagueFeed>> ReadFollowedAsync(IReadOnlyCollection<SportsFollow> follows, bool refresh, CancellationToken cancellationToken)
    {
        var held = new Dictionary<LeagueId, CachedLeague>();
        foreach (var entry in table.Leagues)
        {
            if (ReadCached(entry.League) is { } cached)
            {
                held[entry.League] = cached;
            }
        }

        if (refresh && SportsPage.Unlocated(held.Values.Select(entry => entry.Feed), follows).Count > 0)
        {
            foreach (var entry in table.Leagues.Where(entry => !held.ContainsKey(entry.League)))
            {
                if (await DiscoverAsync(entry.League, cancellationToken) is { } found)
                {
                    held[entry.League] = found;
                }
            }
        }

        var viewers = follows.Select(follow => follow.Viewer).Distinct().ToList();
        var followed = viewers
            .SelectMany(viewer => SportsFollowing.FollowedLeagues(held.Values.Select(entry => entry.Feed), follows, viewer))
            .Distinct()
            .ToList();

        var feeds = new List<LeagueFeed>();
        foreach (var league in followed)
        {
            var current = held[league];
            if (refresh && SportsFreshness.NeedsFetch(current.FetchedAt, current.Feed.League.Phase, clock()))
            {
                current = await RefreshAsync(league, current, cancellationToken);
            }

            feeds.Add(current.Feed);
        }

        return feeds;
    }

    /// <summary>Every league in the table with its teams, for the picker: the cache, else a one-day look at the league.</summary>
    public async Task<IReadOnlyList<LeagueTeams>> BrowseAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var leagues = new List<LeagueTeams>();
            foreach (var entry in table.Leagues)
            {
                var cached = ReadCached(entry.League) ?? await DiscoverAsync(entry.League, cancellationToken);
                if (cached is not null)
                {
                    leagues.Add(new LeagueTeams(cached.Feed.League, cached.Feed.Teams));
                }
            }

            return leagues;
        }
        finally
        {
            gate.Release();
        }
    }

    private CachedLeague? ReadCached(LeagueId league)
    {
        var read = cache.Read(league);
        if (read.Status == CacheReadStatus.Unreadable)
        {
            warn(read.Problem ?? $"The cached league {league} could not be read.", null);
        }

        return read.Entry;
    }

    // One day and no standings: enough to learn the league's teams. Stored as fetched long ago, so the full fetch follows.
    private async Task<CachedLeague?> DiscoverAsync(LeagueId league, CancellationToken cancellationToken)
    {
        try
        {
            var schedule = await source.GetScheduleAsync(league, new SportsWindow(Today(), 0, 0), cancellationToken);
            var feed = LeagueFeeds.Assemble(schedule, [], []);
            Store(feed, DateTimeOffset.UnixEpoch);
            return new CachedLeague(feed, DateTimeOffset.UnixEpoch);
        }
        catch (CatalogueLookupException exception)
        {
            warn($"League {league} could not be looked at ({exception.Failure}); its teams are unknown until it can be.", exception);
            return null;
        }
    }

    private async Task<CachedLeague> RefreshAsync(LeagueId league, CachedLeague current, CancellationToken cancellationToken)
    {
        LeagueSchedule schedule;
        try
        {
            schedule = await source.GetScheduleAsync(league, SportsWindow.Around(Today()), cancellationToken);
        }
        catch (CatalogueLookupException exception)
        {
            warn($"League {league} could not be refreshed ({exception.Failure}); the cached copy stands.", exception);
            return current;
        }

        IReadOnlyList<Team> order;
        try
        {
            order = await source.GetStandingsOrderAsync(league, cancellationToken);
        }
        catch (CatalogueLookupException exception)
        {
            warn($"League {league} has no fresh standings ({exception.Failure}); the last rank order stands.", exception);
            order = current.Feed.Standings;
        }

        var now = clock();
        var feed = LeagueFeeds.Assemble(schedule, order, current.Feed.Highlights);
        Store(feed, now);
        return new CachedLeague(feed, now);
    }

    // A cache that cannot be written costs only the next first paint; the feed in hand is still good, so it is logged and returned.
    private void Store(LeagueFeed feed, DateTimeOffset fetchedAt)
    {
        try
        {
            cache.Store(feed, fetchedAt);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            warn($"League {feed.League.Id} could not be saved to the cache ({exception.Message}); it is used as fetched.", exception);
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock().DateTime);
}
