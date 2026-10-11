using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Calendar;

/// <summary>
/// Each adult's own held leagues, for the household calendar (D70, D242). <see cref="CalendarItems.ForHousehold"/>
/// throws for an adult who follows a game and has no entry — "holding nothing" and "not known" are different
/// answers — so every adult asked about gets one, an empty set when they hold nothing. A league is held for
/// an adult by the Sports rule itself (<see cref="SpoilerHold.IsHeld"/>), over that adult's own follows and
/// their own watch evidence for the recordings, never anyone else's.
/// </summary>
public static class CalendarScreenHolds
{
    public static IReadOnlyDictionary<ViewerId, IReadOnlySet<LeagueId>> ByAdult(
        IEnumerable<ViewerId> adults,
        IEnumerable<LeagueFeed> feeds,
        IReadOnlyCollection<SportsFollow> follows,
        Func<ViewerId, IEnumerable<WatchEvent>> sittingsOf,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(adults);
        ArgumentNullException.ThrowIfNull(feeds);
        ArgumentNullException.ThrowIfNull(follows);
        ArgumentNullException.ThrowIfNull(sittingsOf);

        var leagues = feeds.DistinctBy(feed => feed.League.Id).ToList();
        var held = new Dictionary<ViewerId, IReadOnlySet<LeagueId>>();
        foreach (var adult in adults.Distinct())
        {
            var teams = follows.Where(follow => follow.Viewer == adult).Select(follow => follow.Team).Distinct().ToList();
            var evidence = sittingsOf(adult).ToList();
            held[adult] = leagues
                .Where(feed => SpoilerHold.IsHeld(feed, teams, RecordedGames.Unwatched(feed, teams, evidence, now), now))
                .Select(feed => feed.League.Id)
                .ToHashSet();
        }

        return held;
    }
}
