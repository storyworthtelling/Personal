using Pulp.Domain.Core;
using Pulp.Domain.Library;

namespace Pulp.Domain.Sports;

/// <summary>
/// What the Sports tab is handed: one <see cref="LeagueSheet"/> — held or open, never a raw feed — for
/// each league the viewer follows, in <see cref="LeagueOrder"/>, and the fold the tab starts from.
/// The screen has no way to reach a standing or a highlight of a league the hold covers, because it is
/// given nothing else.
/// </summary>
public sealed record SportsPage(IReadOnlyList<LeagueSheet> Sheets, LeagueFold Fold)
{
    /// <summary>
    /// The page for <paramref name="viewer"/>: leagues with a team they follow (D67, D60), each presented
    /// through the spoiler hold with the recordings their watch evidence leaves unwatched
    /// (<see cref="RecordedGames"/>). Drawing a league spends its peek, if the Spoilers tag was tapped
    /// (<see cref="SpoilerPeeks"/>). The clock is an argument.
    /// </summary>
    public static SportsPage Build(
        IEnumerable<LeagueFeed> feeds,
        IReadOnlyCollection<SportsFollow> follows,
        ViewerId viewer,
        IEnumerable<WatchEvent> sittings,
        DateTimeOffset now,
        SpoilerPeeks peeks,
        FoldMemory memory)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        ArgumentNullException.ThrowIfNull(follows);
        ArgumentNullException.ThrowIfNull(sittings);
        ArgumentNullException.ThrowIfNull(peeks);
        ArgumentNullException.ThrowIfNull(memory);
        var all = feeds.ToList();
        var teams = follows.Where(follow => follow.Viewer == viewer).Select(follow => follow.Team).Distinct().ToList();
        var followed = SportsFollowing.FollowedLeagues(all, follows, viewer).ToHashSet();
        var mine = all.Where(feed => followed.Contains(feed.League.Id)).DistinctBy(feed => feed.League.Id).ToDictionary(feed => feed.League.Id);
        var evidence = sittings.ToList();

        var ordered = LeagueOrder.Order(mine.Values.Select(feed => feed.League));
        var sheets = ordered
            .Select(league => mine[league.Id])
            .Select(feed => SpoilerHold.Present(feed, teams, RecordedGames.Unwatched(feed, teams, evidence, now), now, peeks.Spend(feed.League.Id), RecordedGames.Watched(feed, teams, evidence, now)))
            .ToList();
        return new SportsPage(sheets, LeagueFold.Starting(ordered, memory));
    }

    /// <summary>
    /// The followed teams no feed in hand lists — what a head must go and find a league for before it can
    /// say which leagues the viewer follows (a team id alone does not name its league, D185).
    /// </summary>
    public static IReadOnlyList<TeamId> Unlocated(IEnumerable<LeagueFeed> feeds, IEnumerable<SportsFollow> follows)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        ArgumentNullException.ThrowIfNull(follows);
        var known = feeds.SelectMany(feed => feed.Teams).Select(team => team.Id).ToHashSet();
        return follows.Select(follow => follow.Team).Distinct().Where(team => !known.Contains(team)).ToList();
    }
}
