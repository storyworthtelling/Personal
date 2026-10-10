using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using static Pulp.Tests.Library.LibraryFixtures;

namespace Pulp.Tests.Library;

/// <summary>
/// How one playlist entry stands for the viewer, which is what <see cref="Playlists.Rows"/> strikes and
/// dims by: a film watched by <c>Watched.Decide</c>'s verdict (D101, D278); a show struck only when
/// finished for good (D153); a show the viewer is caught up on dims until its next season lands
/// (D224), and one with episodes left is never dimmed by an upcoming season.
/// </summary>
public class PlaylistPageStandingTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static readonly PlaylistEntry FilmEntry = PlaylistEntry.ForTitle(Larkspur, TitleKind.Film);
    private static readonly PlaylistEntry ShowEntry = PlaylistEntry.ForTitle(Lighthouse, TitleKind.Show);

    private static CatalogueEpisode Aired(int season, int number, DateTimeOffset at) =>
        new(E(season, number), $"Episode {number}", string.Empty, at.Year, DateOnly.FromDateTime(at.UtcDateTime), at, []);

    /// <summary>Season one aired (three episodes, weekly, ending a fortnight ago) and season two announced.</summary>
    private static CatalogueTitle Lighthouses(bool ended = false, DateTimeOffset? seasonTwo = null)
    {
        var first = DaysAgo(28);
        var seasons = new List<CatalogueSeason>
        {
            new(1, "Season 1", [Aired(1, 1, first), Aired(1, 2, first.AddDays(7)), Aired(1, 3, first.AddDays(14))]),
        };
        if (seasonTwo is { } out2)
        {
            seasons.Add(new CatalogueSeason(2, "Season 2", [Aired(2, 1, out2), Aired(2, 2, out2.AddDays(7))]));
        }

        return new CatalogueTitle
        {
            Id = Lighthouse,
            Kind = TitleKind.Show,
            Name = "The Lighthouse Keepers",
            Seasons = seasons,
            Ended = ended,
        };
    }

    [Fact]
    public void AFilmSeenToTheEndIsWatched()
    {
        var standing = PlaylistPageStanding.For(FilmEntry, [FilmFinished(Larkspur, DaysAgo(2))], title: null, Now, Utc);

        Assert.True(standing.Watched);
    }

    [Fact]
    public void AFilmMarkedUnwatchedAfterwardsIsNotWatched()
    {
        var standing = PlaylistPageStanding.For(
            FilmEntry,
            [
                FilmFinished(Larkspur, DaysAgo(2)),
                new WatchEvent(Larkspur, WatchedKind.Film, DaysAgo(1), null, Evidence: WatchSource.MarkedUnwatched),
            ],
            title: null,
            Now,
            Utc);

        Assert.False(standing.Watched);
    }

    [Fact]
    public void AFilmNeverWatchedStandsUnknown()
    {
        Assert.Equal(EntryStanding.Unknown, PlaylistPageStanding.For(FilmEntry, [], title: null, Now, Utc));
    }

    [Fact]
    public void AShowCaughtUpWithASeasonComingDimsUntilThatSeason()
    {
        var seasonTwo = Now.AddDays(10);
        var standing = PlaylistPageStanding.For(
            ShowEntry,
            [Episode(Lighthouse, E(1, 1), DaysAgo(20)), Episode(Lighthouse, E(1, 2), DaysAgo(12)), Episode(Lighthouse, E(1, 3), DaysAgo(5))],
            Lighthouses(seasonTwo: seasonTwo),
            Now,
            Utc);

        Assert.True(standing.CaughtUp);
        Assert.False(standing.Watched);
        Assert.Equal(2, standing.NextSeason);
        Assert.Equal(DateOnly.FromDateTime(seasonTwo.UtcDateTime), standing.NextSeasonOut);
    }

    [Fact]
    public void AShowWithEpisodesLeftIsNotCaughtUpWhateverIsComing()
    {
        var standing = PlaylistPageStanding.For(
            ShowEntry,
            [Episode(Lighthouse, E(1, 1), DaysAgo(20))],
            Lighthouses(seasonTwo: Now.AddDays(10)),
            Now,
            Utc);

        Assert.False(standing.CaughtUp);
        Assert.False(standing.Watched);
    }

    [Fact]
    public void AShowNobodyHasStartedIsNotCaughtUp()
    {
        var standing = PlaylistPageStanding.For(ShowEntry, [], Lighthouses(seasonTwo: Now.AddDays(10)), Now, Utc);

        Assert.False(standing.CaughtUp);
    }

    [Fact]
    public void AnEndedShowWatchedToTheEndIsWatchedForGood()
    {
        var standing = PlaylistPageStanding.For(
            ShowEntry,
            [Episode(Lighthouse, E(1, 1), DaysAgo(20)), Episode(Lighthouse, E(1, 2), DaysAgo(12)), Episode(Lighthouse, E(1, 3), DaysAgo(5))],
            Lighthouses(ended: true),
            Now,
            Utc);

        Assert.True(standing.Watched);
    }

    [Fact]
    public void ARunningShowWatchedToDateIsCaughtUpNotWatched()
    {
        var standing = PlaylistPageStanding.For(
            ShowEntry,
            [Episode(Lighthouse, E(1, 1), DaysAgo(20)), Episode(Lighthouse, E(1, 2), DaysAgo(12)), Episode(Lighthouse, E(1, 3), DaysAgo(5))],
            Lighthouses(ended: false),
            Now,
            Utc);

        Assert.False(standing.Watched);
        Assert.True(standing.CaughtUp);
        Assert.Null(standing.NextSeason);
    }

    [Fact]
    public void AShowWhoseScheduleIsUnknownStandsUnknown()
    {
        var standing = PlaylistPageStanding.For(ShowEntry, [Episode(Lighthouse, E(1, 1), DaysAgo(2))], title: null, Now, Utc);

        Assert.Equal(EntryStanding.Unknown, standing);
    }
}
