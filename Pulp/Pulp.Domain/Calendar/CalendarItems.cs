using Pulp.Domain.Core;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Calendar;

/// <summary>
/// What goes on the calendar (D146): new episodes of shows on Continue Watching, releases of
/// playlist titles, followed games and plans — two weeks ahead (D245), timed where a time is known
/// and all-day otherwise (D148). Two readings of the same sources:
/// <list type="bullet">
/// <item><see cref="ForViewer"/> — the Calendar page: the viewer's own items and shared ones only
/// (D244). The other adult's private shows, teams and playlists stay off it.</item>
/// <item><see cref="ForHousehold"/> — the household Google calendar Foreword prints: everyone's,
/// each labelled with whose it is (D147).</item>
/// </list>
/// A held league's revealing games are absent from both (D242); its championship keeps its date,
/// under the championship's own name.
/// </summary>
public static class CalendarItems
{
    /// <summary>D245: today and the thirteen days after it.</summary>
    public const int DaysAhead = 14;

    public static IReadOnlyList<CalendarItem> ForViewer(
        CalendarSources sources,
        ViewerId viewer,
        DateOnly today,
        TimeZoneInfo zone,
        IReadOnlySet<LeagueId> heldLeagues) =>
        Build(sources, viewer, today, zone, heldLeagues);

    public static IReadOnlyList<CalendarItem> ForHousehold(
        CalendarSources sources,
        DateOnly today,
        TimeZoneInfo zone,
        IReadOnlySet<LeagueId> heldLeagues) =>
        Build(sources, null, today, zone, heldLeagues);

    /// <summary>Groups items under their day: all-day items first, then by time.</summary>
    public static IReadOnlyList<CalendarDay> ByDay(IEnumerable<CalendarItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Ordered(items)
            .GroupBy(item => item.When.Date)
            .Select(group => new CalendarDay(group.Key, group.ToList()))
            .ToList();
    }

    private static List<CalendarItem> Build(
        CalendarSources sources,
        ViewerId? viewer,
        DateOnly today,
        TimeZoneInfo zone,
        IReadOnlySet<LeagueId> heldLeagues)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(heldLeagues);

        var items = new List<CalendarItem>();
        items.AddRange(sources.Episodes
            .Where(episode => viewer is null || episode.Watchers.Contains(viewer.Value))
            .Select(episode => FromEpisode(episode, zone)));
        items.AddRange(sources.Releases
            .Select(release => FromRelease(release, viewer, zone))
            .OfType<CalendarItem>());
        items.AddRange(sources.Games
            .Where(game => viewer is null || game.Followers.Contains(viewer.Value))
            .Where(game => !IsHeldBack(game, heldLeagues))
            .Select(game => FromGame(game, zone)));
        items.AddRange(sources.Plans
            .Where(plan => viewer is null || plan.Viewers.Contains(viewer.Value))
            .Select(FromPlan));

        var end = today.AddDays(DaysAhead);
        var inWindow = items.Where(item => item.When.Date >= today && item.When.Date < end);
        return Ordered(Merge(inWindow)).ToList();
    }

    /// <summary>
    /// D237/D242: a playoff fixture of a held league says who won the round before, so it is absent.
    /// The championship stays (D239) — its line carries the championship's name, never its teams.
    /// </summary>
    private static bool IsHeldBack(Fixture game, IReadOnlySet<LeagueId> heldLeagues) =>
        heldLeagues.Contains(game.League)
        && SpoilerHold.Reveals(game.Stage)
        && game.Stage != GameStage.Championship;

    private static CalendarItem FromEpisode(EpisodeRelease episode, TimeZoneInfo zone) => new(
        "episode:" + Lower(episode.Show.Value) + ":" + episode.Season + ":" + episode.Episode,
        CalendarItemKind.NewEpisode,
        When(episode.Date, episode.LivePremiere, zone),
        episode.ShowName,
        "S" + episode.Season + " E" + episode.Episode,
        episode.Service,
        episode.Show,
        null,
        Sorted(episode.Watchers),
        []);

    /// <summary>
    /// A release shows on the page when one of its playlists is the viewer's or shared, and names only
    /// those playlists; on the household calendar it names them all.
    /// </summary>
    private static CalendarItem? FromRelease(TitleRelease release, ViewerId? viewer, TimeZoneInfo zone)
    {
        var shelves = release.Playlists
            .Where(shelf => viewer is null || shelf.Owner == viewer.Value || shelf.Shared)
            .ToList();
        if (shelves.Count == 0)
        {
            return null;
        }

        return new CalendarItem(
            "release:" + Lower(release.Title.Value),
            CalendarItemKind.Release,
            When(release.Date, release.LivePremiere, zone),
            release.Name,
            null,
            release.Service,
            release.Title,
            null,
            Sorted(shelves.Select(shelf => shelf.Owner)),
            shelves.Select(shelf => shelf.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static CalendarItem FromGame(Fixture game, TimeZoneInfo zone) => new(
        "game:" + Lower(game.Id.Value),
        CalendarItemKind.Game,
        CalendarWhen.At(game.StartsAt, zone),
        game.Name,
        string.IsNullOrEmpty(game.Channel) ? null : game.Channel,
        null,
        null,
        game.League,
        Sorted(game.Followers),
        []);

    private static CalendarItem FromPlan(PlannedViewing plan) => new(
        "plan:" + Lower(plan.PlanId),
        CalendarItemKind.Plan,
        new CalendarWhen(plan.Date, plan.Time),
        plan.Name,
        null,
        null,
        plan.Title,
        null,
        Sorted(plan.Viewers),
        []);

    /// <summary>D148: timed only for a live premiere; a streaming drop is all-day.</summary>
    private static CalendarWhen When(DateOnly date, DateTimeOffset? livePremiere, TimeZoneInfo zone) =>
        livePremiere is { } instant ? CalendarWhen.At(instant, zone) : CalendarWhen.AllDay(date);

    /// <summary>The same thing named twice (one show on both adults' lists) is one line for both.</summary>
    private static IEnumerable<CalendarItem> Merge(IEnumerable<CalendarItem> items) =>
        items
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.First() with
            {
                Whose = Sorted(group.SelectMany(item => item.Whose)),
                Playlists = group.SelectMany(item => item.Playlists).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            });

    private static IEnumerable<CalendarItem> Ordered(IEnumerable<CalendarItem> items) =>
        items
            .OrderBy(item => item.When.Date)
            .ThenBy(item => item.When.IsAllDay ? 0 : 1)
            .ThenBy(item => item.When.Time ?? TimeOnly.MinValue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Key, StringComparer.Ordinal);

    private static List<ViewerId> Sorted(IEnumerable<ViewerId> viewers) =>
        viewers.Distinct().OrderBy(viewer => viewer.Value, StringComparer.OrdinalIgnoreCase).ToList();

    private static string Lower(string value) => value.ToLowerInvariant();
}
