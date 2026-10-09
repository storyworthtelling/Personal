using Pulp.Domain.Core;
using Pulp.Domain.Library;
using static Pulp.Tests.Library.LibraryFixtures;

namespace Pulp.Tests.Library;

/// <summary>
/// D226 kept forever; D227 one row per show under the day it was last watched, with the last episode
/// and a whole-show progress mark until it is complete; D228 films and shows only.
/// </summary>
public class RecentlyWatchedTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static readonly IReadOnlyDictionary<TitleId, int> Counts = new Dictionary<TitleId, int>
    {
        [Lighthouse] = 14,
        [Ferry] = 3,
    };

    [Fact]
    public void OneRowPerShowShowingTheLastEpisodeAndTheWholeShowMark()
    {
        var days = RecentlyWatched.Build(
        [
            Episode(Lighthouse, E(1, 1), DaysAgo(3)),
            Episode(Lighthouse, E(1, 2), DaysAgo(0.1)),
        ],
        Counts,
        Utc);

        var row = Assert.Single(Assert.Single(days).Rows);
        Assert.Equal(E(1, 2), row.LastEpisode);
        Assert.Equal(new ProgressMark(2, 14, ProgressUnit.Episode), row.Progress);
        Assert.False(row.ShowFinished);
    }

    [Fact]
    public void RowsAreGroupedUnderTheDayTheyWereLastWatchedNewestFirst()
    {
        var days = RecentlyWatched.Build(
        [
            Episode(Lighthouse, E(1, 1), DaysAgo(3)),
            FilmFinished(Tidewater, DaysAgo(3).AddHours(-2)),
            FilmStopped(Larkspur, DaysAgo(1), 46, 118),
            Episode(Lighthouse, E(1, 2), DaysAgo(0.1)),
        ],
        Counts,
        Utc);

        Assert.Equal(
            [DateOnly.FromDateTime(DaysAgo(0.1).UtcDateTime), DateOnly.FromDateTime(DaysAgo(1).UtcDateTime), DateOnly.FromDateTime(DaysAgo(3).UtcDateTime)],
            days.Select(d => d.Day));
        Assert.Equal([Lighthouse], days[0].Rows.Select(r => r.Title));
        Assert.Equal([Tidewater], days[2].Rows.Select(r => r.Title));
    }

    [Fact]
    public void TheDayIsReadInTheViewersZone()
    {
        // 02:00 UTC is still the previous evening five hours west.
        var at = new DateTimeOffset(2026, 3, 12, 2, 0, 0, TimeSpan.Zero);
        var west = TimeZoneInfo.CreateCustomTimeZone("example-west", TimeSpan.FromHours(-5), "example-west", "example-west");

        var days = RecentlyWatched.Build([FilmFinished(Tidewater, at)], Counts, west);

        Assert.Equal(new DateOnly(2026, 3, 11), Assert.Single(days).Day);
    }

    [Fact]
    public void NothingAgesOut()
    {
        var days = RecentlyWatched.Build([FilmFinished(Tidewater, DaysAgo(365 * 30))], Counts, Utc);

        Assert.Equal(Tidewater, Assert.Single(Assert.Single(days).Rows).Title);
    }

    [Fact]
    public void GamesAreNotListed()
    {
        var game = new WatchEvent(Pennant, WatchedKind.Game, DaysAgo(1), Streamly);

        var days = RecentlyWatched.Build([game, FilmFinished(Tidewater, DaysAgo(2))], Counts, Utc);

        Assert.DoesNotContain(days.SelectMany(d => d.Rows), r => r.Title == Pennant);
        Assert.Single(days.SelectMany(d => d.Rows));
    }

    [Fact]
    public void ACompleteShowReadsFinishedWithNoMark()
    {
        var days = RecentlyWatched.Build(
            Enumerable.Range(1, 3).Select(n => Episode(Ferry, E(1, n), DaysAgo(4 - n))),
            Counts,
            Utc);

        var row = days.SelectMany(d => d.Rows).Single();
        Assert.True(row.ShowFinished);
        Assert.Null(row.Progress);
    }

    [Fact]
    public void AFilmSaysHowItEnded()
    {
        var marked = new WatchEvent(Larkspur, WatchedKind.Film, DaysAgo(5), null, Evidence: WatchSource.MarkedWatched);

        var rows = RecentlyWatched.Build(
            [FilmFinished(Tidewater, DaysAgo(1)), FilmStopped(Larkspur, DaysAgo(2), 46, 118), marked],
            Counts,
            Utc).SelectMany(d => d.Rows).ToList();

        var tidewater = rows.Single(r => r.Title == Tidewater);
        var larkspur = rows.Single(r => r.Title == Larkspur);
        Assert.Equal(FilmEnding.Finished, tidewater.Ending);
        Assert.Equal(FilmEnding.Stopped, larkspur.Ending);
        Assert.Equal(new ProgressMark(46, 118, ProgressUnit.Minute), larkspur.Progress);
        Assert.Equal(Streamly, larkspur.Service);
    }

    [Fact]
    public void AFilmLastMarkedByHandSaysSo()
    {
        var marked = new WatchEvent(Larkspur, WatchedKind.Film, DaysAgo(1), null, Evidence: WatchSource.MarkedWatched);

        var row = RecentlyWatched.Build([FilmStopped(Larkspur, DaysAgo(2), 46, 118), marked], Counts, Utc)
            .SelectMany(d => d.Rows).Single();

        Assert.Equal(FilmEnding.MarkedWatched, row.Ending);
    }

    [Fact]
    public void AShowWithNoKnownEpisodeCountHasNoMark()
    {
        var row = RecentlyWatched.Build([Episode(new TitleId("uncounted-example"), E(1, 1), DaysAgo(1))], Counts, Utc)
            .SelectMany(d => d.Rows).Single();

        Assert.Null(row.Progress);
        Assert.False(row.ShowFinished);
    }
}
