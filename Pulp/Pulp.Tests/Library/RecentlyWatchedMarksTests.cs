using Pulp.Domain.Core;
using Pulp.Domain.Library;
using static Pulp.Tests.Library.LibraryFixtures;

namespace Pulp.Tests.Library;

/// <summary>
/// What a long-press on Recently watched writes (mockup 05, D160): "Mark unwatched" is a mark by hand
/// (D278) — on a film one whole-title mark, on a show one per episode the verdict calls watched — and
/// it is not a sitting, so the row keeps its day. "Remove from history" takes every one of the viewer's
/// records of that title and nothing else.
/// </summary>
public class RecentlyWatchedMarksTests
{
    [Fact]
    public void UnwatchingAFilmIsOneMarkByHand()
    {
        var marks = RecentlyWatchedMarks.Unwatch([FilmFinished(Tidewater, DaysAgo(2))], Tidewater, Now);

        var mark = Assert.Single(marks);
        Assert.Equal(WatchSource.MarkedUnwatched, mark.Evidence);
        Assert.Equal(WatchedKind.Film, mark.Kind);
        Assert.Null(mark.Episode);
        Assert.Equal(Now, mark.At);
    }

    [Fact]
    public void UnwatchingAShowMarksEachWatchedEpisodeAndNoOther()
    {
        var marks = RecentlyWatchedMarks.Unwatch(
        [
            Episode(Lighthouse, E(1, 1), DaysAgo(4)),
            Episode(Lighthouse, E(1, 2), DaysAgo(3)),
            PartEpisode(Lighthouse, E(1, 3), DaysAgo(2)),
            Episode(Ferry, E(1, 1), DaysAgo(1)),
        ],
        Lighthouse,
        Now);

        Assert.Equal([E(1, 1), E(1, 2)], marks.Select(mark => mark.Episode).OrderBy(e => e));
        Assert.All(marks, mark => Assert.Equal(WatchSource.MarkedUnwatched, mark.Evidence));
        Assert.All(marks, mark => Assert.Equal(Lighthouse, mark.Title));
    }

    [Fact]
    public void AfterUnwatchingTheRowKeepsItsDay()
    {
        var sittings = new List<WatchEvent> { FilmFinished(Tidewater, DaysAgo(2)) };
        sittings.AddRange(RecentlyWatchedMarks.Unwatch(sittings, Tidewater, Now));

        var day = Assert.Single(RecentlyWatched.Build(sittings, new Dictionary<TitleId, int>(), TimeZoneInfo.Utc));
        Assert.Equal(DateOnly.FromDateTime(DaysAgo(2).UtcDateTime), day.Day);
        Assert.Equal(FilmEnding.MarkedUnwatched, Assert.Single(day.Rows).Ending);
    }

    [Fact]
    public void AShowWithNothingWatchedHasNothingToUnwatch()
    {
        Assert.Empty(RecentlyWatchedMarks.Unwatch([PartEpisode(Lighthouse, E(1, 1), DaysAgo(1))], Lighthouse, Now));
    }

    [Fact]
    public void HistoryIsEveryRecordOfThatTitleAndNoOther()
    {
        var mark = new WatchEvent(Tidewater, WatchedKind.Film, DaysAgo(1), null, Evidence: WatchSource.MarkedWatched);
        var history = RecentlyWatchedMarks.History(
            [FilmFinished(Tidewater, DaysAgo(3)), mark, FilmStopped(Larkspur, DaysAgo(2), 46, 118)],
            Tidewater);

        Assert.Equal(2, history.Count);
        Assert.All(history, sitting => Assert.Equal(Tidewater, sitting.Title));
    }
}
