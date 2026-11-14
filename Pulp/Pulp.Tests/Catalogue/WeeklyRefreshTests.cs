using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;

namespace Pulp.Tests.Catalogue;

/// <summary>
/// The weekly refresh (D204, audit W7.5, owner's ruling to build it): the titles the household tracks are
/// looked at again in the background once a week, oldest first, and never so many that the month's
/// availability allowance runs short of what opening a title needs.
/// </summary>
public class WeeklyRefreshTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    private static WatchedSitting Watched(string title, WatchedKind kind) =>
        new(new ViewerId("sam"), new WatchEvent(new TitleId(title), kind, Now.AddDays(-3), null));

    [Fact]
    public void TrackedIsEveryShowBeingWatchedAndEveryFilmOrShowOnAPlaylist()
    {
        var list = Playlist.Named("Weekend")
            .Add(PlaylistEntry.ForTitle(new TitleId("film-1"), TitleKind.Film))
            .Add(new PlaylistEntry(PlaylistEntryKind.Game, "game-1"))
            .Add(PlaylistEntry.ForTitle(new TitleId("show-1"), TitleKind.Show));
        var playlists = new[] { new HouseholdPlaylist(new PlaylistId("p1"), new ViewerId("sam"), Shared: true, list) };

        var tracked = WeeklyRefresh.Tracked(
            [Watched("show-1", WatchedKind.Show), Watched("show-2", WatchedKind.Show), Watched("film-9", WatchedKind.Film)],
            playlists);

        Assert.Equal(["show-1", "show-2", "film-1"], tracked.Select(id => id.Value));
    }

    [Fact]
    public void OnlyTitlesAWeekStaleAreDue_OldestAndNeverFetchedFirst()
    {
        var due = WeeklyRefresh.Due(
        [
            (new TitleId("fresh"), new CacheStamp(Now.AddDays(-2), false)),
            (new TitleId("old"), new CacheStamp(Now.AddDays(-20), false)),
            (new TitleId("never"), null),
            (new TitleId("week"), new CacheStamp(Now.AddDays(-8), false)),
        ], usedThisMonth: 0, Now);

        Assert.Equal(["never", "old", "week"], due.Select(id => id.Value));
    }

    [Fact]
    public void ARunIsCapped()
    {
        var many = Enumerable.Range(0, 100).Select(i => (new TitleId($"t{i}"), (CacheStamp?)null));

        Assert.Equal(WeeklyRefresh.PerRun, WeeklyRefresh.Due(many, usedThisMonth: 0, Now).Count);
    }

    [Fact]
    public void TheBackgroundStopsShortOfTheAllowanceSoOpeningATitleCanStillLook()
    {
        var many = Enumerable.Range(0, 100).Select(i => (new TitleId($"t{i}"), (CacheStamp?)null)).ToList();

        Assert.Empty(WeeklyRefresh.Due(many, usedThisMonth: WeeklyRefresh.BackgroundCeiling, Now));
        Assert.Equal(5, WeeklyRefresh.Due(many, usedThisMonth: WeeklyRefresh.BackgroundCeiling - 5, Now).Count);
        Assert.True(WeeklyRefresh.BackgroundCeiling < LookupMonth.Allowance);
    }
}
