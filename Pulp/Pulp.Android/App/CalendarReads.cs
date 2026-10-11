using Pulp.Domain.Calendar;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Sports;
using Pulp.Domain.Watching;

namespace Pulp.App;

/// <summary>Everything one read of the household's calendar produced: the sources, whose holds, and what the words need.</summary>
internal sealed record CalendarRead(
    CalendarSources Sources,
    HouseholdView Household,
    IReadOnlyDictionary<ViewerId, IReadOnlySet<LeagueId>> HeldByAdult,
    Func<TitleId, TitleKind?> KindOf,
    TimeZoneInfo Zone);

/// <summary>
/// The one read behind both the Calendar page and the nightly writer, so the page and the household
/// calendar cannot disagree about what is coming (mockup 8). It gathers the household's adults' shows
/// (every show they have sat with, D21), the playlists, the catalogue's copy of each title (cache-first,
/// <see cref="PulpServices.TitleAsync"/>), and the followed leagues' schedules — the raw feeds, which only
/// <c>SportsFollowing.Fixtures</c> and the spoiler holds may touch, and which is why this lives in App and
/// not in a screen. The logic is the Domain's (<c>CalendarScreenSources</c>, <c>CalendarScreenHolds</c>);
/// this is the I/O that feeds it.
/// </summary>
internal sealed class CalendarReads(PulpServices services)
{
    public async Task<CalendarRead> ReadAsync(CancellationToken cancellationToken)
    {
        var household = await services.HouseholdAsync(cancellationToken);
        var zone = TimeZoneInfo.Local;
        var now = DateTimeOffset.Now;
        var roster = household.Roster;
        var adults = roster?.Viewers.Select(viewer => viewer.Id).Where(id => !roster.IsChild(id)).ToList() ?? [];

        var showsByAdult = adults.ToDictionary(
            adult => adult,
            adult => (IReadOnlyList<TitleId>)household.SittingsOf(adult)
                .Where(sitting => sitting.Kind == WatchedKind.Show)
                .Select(sitting => sitting.Title)
                .Distinct()
                .ToList());
        var playlists = adults.SelectMany(household.PlaylistsFor).DistinctBy(playlist => playlist.Id).ToList();

        var tracked = showsByAdult.Values.SelectMany(shows => shows).ToHashSet();
        var wanted = tracked
            .Concat(playlists.SelectMany(playlist => playlist.List.Entries)
                .Where(entry => entry.Kind == PlaylistEntryKind.Show)
                .Select(entry => new TitleId(entry.Key)))
            .Distinct()
            .ToList();
        var titles = new Dictionary<TitleId, CatalogueTitle>();
        foreach (var id in wanted)
        {
            if (await services.TitleAsync(id, tracked.Contains(id), opened: false, cancellationToken) is { } title)
            {
                titles[id] = title;
            }
        }

        var follows = CalendarFollows.OfAdults(household.SportsFollows, roster);
        IReadOnlyList<LeagueFeed> feeds = follows.Count == 0
            ? []
            : await services.FollowedFeedsAsync(follows, refresh: true, cancellationToken);
        var held = CalendarScreenHolds.ByAdult(adults, feeds, follows, household.SittingsOf, now);
        var games = SportsFollowing.Fixtures(feeds, follows, now);

        var reads = new CalendarScreenReads(
            showsByAdult,
            playlists,
            titles,
            title => ContinueRowText.Home(title, household.ServicePlans, household.HomeOverrides)?.Service,
            zone);
        return new CalendarRead(
            CalendarScreenSources.Build(reads, games),
            household,
            held,
            id => titles.TryGetValue(id, out var title) ? title.Kind : null,
            zone);
    }
}
