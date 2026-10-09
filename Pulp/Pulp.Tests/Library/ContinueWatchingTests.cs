using Pulp.Domain.Core;
using Pulp.Domain.Library;
using static Pulp.Tests.Library.LibraryFixtures;

namespace Pulp.Tests.Library;

/// <summary>
/// D96 most recently watched first, a new episode jumps to the top; D150 the progress mark; D152 a
/// caught-up show leaves and comes back when an episode lands; D153 a finished one leaves for good.
/// </summary>
public class ContinueWatchingTests
{
    private static ShowSchedule Running(TitleId show, int episodes, DateTimeOffset first) =>
        new(show, Season(1, episodes, first).ToList(), Ended: false);

    [Fact]
    public void MostRecentlyWatchedComesFirst()
    {
        var rows = ContinueWatching.Build(
        [
            Episode(Lighthouse, E(1, 1), DaysAgo(5)),
            Episode(Ferry, E(1, 1), DaysAgo(2)),
            FilmStopped(Larkspur, DaysAgo(3), 46, 118),
        ],
        [Running(Lighthouse, 6, DaysAgo(100)), Running(Ferry, 6, DaysAgo(100))],
        Now);

        Assert.Equal([Ferry, Larkspur, Lighthouse], rows.Select(r => r.Title));
    }

    [Fact]
    public void ANewEpisodeJumpsItsShowToTheTopAndSaysSo()
    {
        // Lighthouse was last watched five days ago; its E3 aired an hour ago.
        var lighthouse = new ShowSchedule(
            Lighthouse,
            [
                new ScheduledEpisode(E(1, 1), DaysAgo(30)),
                new ScheduledEpisode(E(1, 2), DaysAgo(20)),
                new ScheduledEpisode(E(1, 3), Now.AddHours(-1)),
            ],
            Ended: false);

        var rows = ContinueWatching.Build(
        [
            Episode(Lighthouse, E(1, 1), DaysAgo(6)),
            Episode(Lighthouse, E(1, 2), DaysAgo(5)),
            Episode(Ferry, E(1, 1), DaysAgo(1)),
        ],
        [lighthouse, Running(Ferry, 6, DaysAgo(100))],
        Now);

        Assert.Equal(Lighthouse, rows[0].Title);
        Assert.True(rows[0].NewEpisode);
        Assert.False(rows[1].NewEpisode);
    }

    [Fact]
    public void AnEpisodeNotYetAiredIsNotNewAndDoesNotJump()
    {
        var lighthouse = new ShowSchedule(
            Lighthouse,
            [new ScheduledEpisode(E(1, 1), DaysAgo(30)), new ScheduledEpisode(E(1, 2), Now.AddDays(2))],
            Ended: false);

        var rows = ContinueWatching.Build([Episode(Lighthouse, E(1, 1), DaysAgo(6))], [lighthouse], Now);

        Assert.Empty(rows);
    }

    [Fact]
    public void ACaughtUpShowLeavesAndReturnsWhenAnEpisodeLands()
    {
        var schedule = new ShowSchedule(
            Lighthouse,
            [new ScheduledEpisode(E(1, 1), DaysAgo(30)), new ScheduledEpisode(E(1, 2), Now.AddDays(3))],
            Ended: false);
        WatchEvent[] events = [Episode(Lighthouse, E(1, 1), DaysAgo(6))];

        Assert.Empty(ContinueWatching.Build(events, [schedule], Now));

        var later = ContinueWatching.Build(events, [schedule], Now.AddDays(4));
        var row = Assert.Single(later);
        Assert.Equal(E(1, 2), row.NextUp);
        Assert.True(row.NewEpisode);
    }

    [Fact]
    public void AShowFinishedForGoodLeaves()
    {
        var ended = new ShowSchedule(Lighthouse, Season(1, 2, DaysAgo(60)).ToList(), Ended: true);

        var rows = ContinueWatching.Build(
            [Episode(Lighthouse, E(1, 1), DaysAgo(5)), Episode(Lighthouse, E(1, 2), DaysAgo(4))],
            [ended],
            Now);

        Assert.Empty(rows);
    }

    [Fact]
    public void AFinishedFilmLeavesAndAStoppedOneStays()
    {
        var rows = ContinueWatching.Build(
            [FilmFinished(Tidewater, DaysAgo(1)), FilmStopped(Larkspur, DaysAgo(2), 46, 118)],
            [],
            Now);

        var row = Assert.Single(rows);
        Assert.Equal(Larkspur, row.Title);
        Assert.Equal(new ProgressMark(46, 118, ProgressUnit.Minute), row.Progress);
    }

    [Fact]
    public void AFilmMarkedWatchedByHandLeaves()
    {
        var marked = new WatchEvent(Tidewater, WatchedKind.Film, DaysAgo(1), null, Evidence: WatchSource.MarkedWatched,
            StoppedAt: TimeSpan.FromMinutes(5), Runtime: TimeSpan.FromMinutes(90));

        Assert.Empty(ContinueWatching.Build([FilmStopped(Tidewater, DaysAgo(2), 5, 90), marked], [], Now));
    }

    [Fact]
    public void NextUpFollowsTheFurthestWatchedEpisodeAndCountsTheSeason()
    {
        var schedule = new ShowSchedule(Ferry, Season(2, 8, DaysAgo(100)).ToList(), Ended: false);

        var rows = ContinueWatching.Build(
            [Episode(Ferry, E(2, 1), DaysAgo(9)), Episode(Ferry, E(2, 4), DaysAgo(3))],
            [schedule],
            Now);

        var row = Assert.Single(rows);
        Assert.Equal(E(2, 5), row.NextUp);
        Assert.Equal(new ProgressMark(5, 8, ProgressUnit.Episode), row.Progress);
    }

    [Fact]
    public void AnEpisodeStoppedPartWayIsStillNextUp()
    {
        var schedule = Running(Ferry, 6, DaysAgo(100));

        var rows = ContinueWatching.Build(
            [Episode(Ferry, E(1, 1), DaysAgo(4)), PartEpisode(Ferry, E(1, 2), DaysAgo(3))],
            [schedule],
            Now);

        Assert.Equal(E(1, 2), Assert.Single(rows).NextUp);
    }

    [Fact]
    public void AShowWhoseScheduleIsUnknownStaysWithoutANextUp()
    {
        // D31: it stays until removed while Pulp cannot tell whether anything is left.
        var row = Assert.Single(ContinueWatching.Build([Episode(Lighthouse, E(1, 1), DaysAgo(2))], [], Now));

        Assert.Null(row.NextUp);
        Assert.Null(row.Progress);
        Assert.False(row.NewEpisode);
    }

    [Fact]
    public void GamesAreNotThisListsBusiness()
    {
        var game = new WatchEvent(Pennant, WatchedKind.Game, DaysAgo(1), Streamly, StoppedAt: TimeSpan.FromMinutes(20), Runtime: TimeSpan.FromMinutes(180));

        Assert.Empty(ContinueWatching.Build([game], [], Now));
    }

    [Fact]
    public void TheClockIsAnArgumentNotTheMachinesTime()
    {
        // The same inputs read at two instants give two answers; nothing inside reads the wall clock.
        var schedule = new ShowSchedule(Lighthouse, [new ScheduledEpisode(E(1, 1), new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero))], Ended: false);
        var watch = PartEpisode(Lighthouse, E(1, 1), new DateTimeOffset(2099, 1, 2, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(ContinueWatching.Build([watch], [schedule], Now));
        Assert.Single(ContinueWatching.Build([watch], [schedule], new DateTimeOffset(2099, 1, 3, 0, 0, 0, TimeSpan.Zero)));
    }
}
