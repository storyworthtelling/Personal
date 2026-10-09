using Pulp.Domain.Core;
using Pulp.Domain.Library;
using static Pulp.Tests.Library.LibraryFixtures;

namespace Pulp.Tests.Library;

/// <summary>
/// D103 hand order, new entries at the bottom; D101 watched entries struck through and kept, "hide
/// watched" filters them; D224 a caught-up show dims until its next season lands; D32 an unreleased
/// film dims until release; D153 a show finished for good stays struck.
/// </summary>
public class PlaylistsTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private static readonly PlaylistEntry Crow = PlaylistEntry.ForTitle(new TitleId("crow-show-example"), TitleKind.Show);
    private static readonly PlaylistEntry Majestic = PlaylistEntry.ForTitle(new TitleId("majestic-film-example"), TitleKind.Film);
    private static readonly PlaylistEntry Tide = PlaylistEntry.ForTitle(Tidewater, TitleKind.Film);
    private static readonly PlaylistEntry Orchard = PlaylistEntry.ForTitle(new TitleId("orchard-show-example"), TitleKind.Show);
    private static readonly PlaylistEntry Pass = PlaylistEntry.ForTitle(new TitleId("pass-film-example"), TitleKind.Film);

    private static Playlist Sundays() =>
        Playlist.Named("Rainy Sundays").Add(Crow).Add(Majestic).Add(Tide).Add(Orchard).Add(Pass);

    [Fact]
    public void NewEntriesGoToTheBottomAndADuplicateIsIgnored()
    {
        var list = Sundays().Add(Majestic);

        Assert.Equal([Crow, Majestic, Tide, Orchard, Pass], list.Entries);
    }

    [Fact]
    public void MovingAnEntryKeepsEveryOtherInHandOrder()
    {
        var list = Sundays().Move(Pass, 0).Move(Crow, 4);

        Assert.Equal([Pass, Majestic, Tide, Orchard, Crow], list.Entries);
    }

    [Fact]
    public void MovingPastTheEndIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sundays().Move(Crow, 5));
    }

    [Fact]
    public void RemovingIsTheOnlyWayAnEntryLeaves()
    {
        Assert.Equal([Crow, Majestic, Orchard, Pass], Sundays().Remove(Tide).Entries);
    }

    [Fact]
    public void AnEntryFromAnyKindIsHeld()
    {
        var game = new PlaylistEntry(PlaylistEntryKind.Game, "pennant-game-example");
        var franchise = new PlaylistEntry(PlaylistEntryKind.Franchise, "lantern-series-example");

        Assert.Equal([game, franchise], Playlist.Named("Mixed").Add(game).Add(franchise).Entries);
        Assert.Equal(game, new PlaylistEntry(PlaylistEntryKind.Game, "PENNANT-GAME-EXAMPLE"));
    }

    [Fact]
    public void WatchedEntriesAreStruckAndKeptInPlace()
    {
        var rows = Playlists.Rows(Sundays(), Standing(watched: Tide), Today, hideWatched: false);

        Assert.Equal([Crow, Majestic, Tide, Orchard, Pass], rows.Select(r => r.Entry));
        Assert.True(rows.Single(r => r.Entry == Tide).Struck);
        Assert.Equal(1, rows.Count(r => r.Struck));
    }

    [Fact]
    public void HideWatchedFiltersTheStruckRows()
    {
        var rows = Playlists.Rows(Sundays(), Standing(watched: Tide), Today, hideWatched: true);

        Assert.Equal([Crow, Majestic, Orchard, Pass], rows.Select(r => r.Entry));
    }

    [Fact]
    public void ACaughtUpShowDimsWithItsNextSeason()
    {
        var standing = new EntryStanding(CaughtUp: true, NextSeason: 2, NextSeasonOut: Today.AddDays(2));

        var row = Playlists.Rows(Sundays(), e => e == Orchard ? standing : EntryStanding.Unknown, Today, false)
            .Single(r => r.Entry == Orchard);

        Assert.Equal(RowDim.CaughtUp, row.Dim);
        Assert.Equal(2, row.NextSeason);
        Assert.Equal(Today.AddDays(2), row.DimUntil);
        Assert.False(row.Struck);
    }

    [Fact]
    public void ACaughtUpShowBrightensWhenTheSeasonLands()
    {
        var standing = new EntryStanding(CaughtUp: true, NextSeason: 2, NextSeasonOut: Today.AddDays(2));

        var row = Playlists.Rows(Sundays(), e => e == Orchard ? standing : EntryStanding.Unknown, Today.AddDays(2), false)
            .Single(r => r.Entry == Orchard);

        Assert.Equal(RowDim.None, row.Dim);
    }

    [Fact]
    public void AShowWithEpisodesLeftIsNotDimmedByAnUpcomingSeason()
    {
        var standing = new EntryStanding(CaughtUp: false, NextSeason: 2, NextSeasonOut: Today.AddDays(2));

        var row = Playlists.Rows(Sundays(), e => e == Orchard ? standing : EntryStanding.Unknown, Today, false)
            .Single(r => r.Entry == Orchard);

        Assert.Equal(RowDim.None, row.Dim);
    }

    [Fact]
    public void AnUnreleasedFilmDimsUntilItsDate()
    {
        var standing = new EntryStanding(ReleasesOn: Today.AddDays(10));

        var before = Playlists.Rows(Sundays(), e => e == Pass ? standing : EntryStanding.Unknown, Today, false).Single(r => r.Entry == Pass);
        var after = Playlists.Rows(Sundays(), e => e == Pass ? standing : EntryStanding.Unknown, Today.AddDays(10), false).Single(r => r.Entry == Pass);

        Assert.Equal(RowDim.NotOutYet, before.Dim);
        Assert.Equal(Today.AddDays(10), before.DimUntil);
        Assert.Equal(RowDim.None, after.Dim);
    }

    [Fact]
    public void AStruckRowIsNeverAlsoDimmed()
    {
        // D153: a show finished for good is struck for an easy rewatch, not dimmed as caught up.
        var standing = new EntryStanding(Watched: true, CaughtUp: true);

        var row = Playlists.Rows(Sundays(), e => e == Orchard ? standing : EntryStanding.Unknown, Today, false)
            .Single(r => r.Entry == Orchard);

        Assert.True(row.Struck);
        Assert.Equal(RowDim.None, row.Dim);
    }

    private static Func<PlaylistEntry, EntryStanding> Standing(PlaylistEntry watched) =>
        e => e == watched ? new EntryStanding(Watched: true) : EntryStanding.Unknown;
}
