using Pulp.Domain.Library;

namespace Pulp.Domain.Sports;

/// <summary>Puts a source's schedule and rank order (and the channel's highlights) into the one <see cref="LeagueFeed"/>.</summary>
public static class LeagueFeeds
{
    /// <summary>A standings row naming a team the league does not have is dropped: the order is of this league's teams only.</summary>
    public static LeagueFeed Assemble(LeagueSchedule schedule, IEnumerable<Team> standingsInOrder, IEnumerable<Highlight> highlights)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(standingsInOrder);
        ArgumentNullException.ThrowIfNull(highlights);
        var members = schedule.Teams.ToDictionary(team => team.Id);
        var ranked = standingsInOrder
            .Where(team => members.ContainsKey(team.Id))
            .Select(team => members[team.Id])
            .DistinctBy(team => team.Id)
            .ToList();
        return new LeagueFeed(schedule.League, schedule.Teams, schedule.Games, ranked, highlights.ToList(), schedule.Championship);
    }
}

/// <summary>
/// How often a league is asked about again. A league in season changes daily; an off-season one
/// hardly at all. The screens paint the cache first; this only says whether a fetch follows. The
/// clock is an argument.
/// </summary>
public static class SportsFreshness
{
    public static TimeSpan RefreshEvery(SeasonPhase phase) => phase switch
    {
        SeasonPhase.RegularSeason or SeasonPhase.Postseason => TimeSpan.FromHours(6),
        SeasonPhase.Preseason => TimeSpan.FromHours(12),
        SeasonPhase.OffSeason => TimeSpan.FromHours(24),
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "A season phase SportsFreshness has no interval for."),
    };

    public static bool NeedsFetch(DateTimeOffset? fetchedAt, SeasonPhase phase, DateTimeOffset now)
    {
        if (fetchedAt is null || fetchedAt > now)
        {
            return true;
        }

        return now - fetchedAt.Value >= RefreshEvery(phase);
    }
}

/// <summary>
/// Which of a viewer's followed teams' games are recordings still waiting to be watched (D55, D58).
/// A game that has started is a recording until the watch evidence says it was watched; the same
/// verdict rule as every title (<see cref="Sittings.Verdict"/>). A game older than
/// <see cref="SportsWindow.RecordingDays"/> is left out: the service's own expiry is not knowable, and
/// D58's fallback is two weeks. Pulp records nothing itself — "recorded" means "started and not watched".
/// </summary>
public static class RecordedGames
{
    public static IReadOnlyList<GameId> Unwatched(
        LeagueFeed feed, IReadOnlyCollection<TeamId> followed, IEnumerable<WatchEvent> sittings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(followed);
        ArgumentNullException.ThrowIfNull(sittings);
        var byGame = sittings
            .Where(sitting => sitting.Kind == WatchedKind.Game)
            .ToLookup(sitting => sitting.Title.Value, StringComparer.OrdinalIgnoreCase);
        var oldest = now.AddDays(-SportsWindow.RecordingDays);

        return feed.Schedule
            .Where(game => game.StartsAt <= now && game.StartsAt >= oldest && followed.Any(game.Involves))
            .Where(game => !Sittings.Verdict(byGame[game.Id.Value]).IsWatched)
            .OrderBy(game => game.StartsAt)
            .Select(game => game.Id)
            .ToList();
    }
}
