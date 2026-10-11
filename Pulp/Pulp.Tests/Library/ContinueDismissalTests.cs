using Pulp.Domain.Core;
using Pulp.Domain.Library;
using static Pulp.Tests.Library.LibraryFixtures;

namespace Pulp.Tests.Library;

/// <summary>
/// "Remove from Continue watching" sticks (D31: a show stays until removed). A removed title stays off
/// the list until the viewer sits down with it again — a sitting newer than the removal. A new episode
/// airing does not bring it back: D152's return-on-a-new-episode is for a show that left because it
/// was caught up, not for one the viewer took off by hand. A manual mark is not a sitting (D278).
/// </summary>
public class ContinueDismissalTests
{
    private static ShowSchedule Running(TitleId show, int episodes, DateTimeOffset first) =>
        new(show, Season(1, episodes, first).ToList(), Ended: false);

    [Fact]
    public void ADismissedShowStaysOffTheList()
    {
        var rows = ContinueWatching.Build(
            [Episode(Lighthouse, E(1, 1), DaysAgo(5)), Episode(Ferry, E(1, 1), DaysAgo(4))],
            [Running(Lighthouse, 6, DaysAgo(100)), Running(Ferry, 6, DaysAgo(100))],
            [new ContinueDismissal(Lighthouse, DaysAgo(3))],
            Now);

        Assert.Equal([Ferry], rows.Select(row => row.Title));
    }

    [Fact]
    public void ADismissedFilmStaysOffTheList()
    {
        var rows = ContinueWatching.Build(
            [FilmStopped(Larkspur, DaysAgo(5), 30, 110)],
            [],
            [new ContinueDismissal(Larkspur, DaysAgo(2))],
            Now);

        Assert.Empty(rows);
    }

    [Fact]
    public void ANewEpisodeAiringDoesNotBringADismissedShowBack()
    {
        // Caught up to E2 five days ago, removed three days ago; E3 aired an hour ago.
        var lighthouse = new ShowSchedule(
            Lighthouse,
            [
                new ScheduledEpisode(E(1, 1), DaysAgo(30)),
                new ScheduledEpisode(E(1, 2), DaysAgo(20)),
                new ScheduledEpisode(E(1, 3), Now.AddHours(-1)),
            ],
            Ended: false);

        var rows = ContinueWatching.Build(
            [Episode(Lighthouse, E(1, 1), DaysAgo(6)), Episode(Lighthouse, E(1, 2), DaysAgo(5))],
            [lighthouse],
            [new ContinueDismissal(Lighthouse, DaysAgo(3))],
            Now);

        Assert.Empty(rows);
    }

    [Fact]
    public void ASittingNewerThanTheRemovalBringsItBack()
    {
        var rows = ContinueWatching.Build(
            [Episode(Lighthouse, E(1, 1), DaysAgo(5)), Episode(Lighthouse, E(1, 2), DaysAgo(1))],
            [Running(Lighthouse, 6, DaysAgo(100))],
            [new ContinueDismissal(Lighthouse, DaysAgo(3))],
            Now);

        var row = Assert.Single(rows);
        Assert.Equal(Lighthouse, row.Title);
        Assert.Equal(E(1, 3), row.NextUp);
    }

    [Fact]
    public void AHandOffNewerThanTheRemovalBringsAFilmBack()
    {
        var rows = ContinueWatching.Build(
            [
                FilmStopped(Larkspur, DaysAgo(5), 30, 110),
                new WatchEvent(Larkspur, WatchedKind.Film, DaysAgo(1), Streamly, Evidence: WatchSource.HandOff),
            ],
            [],
            [new ContinueDismissal(Larkspur, DaysAgo(2))],
            Now);

        Assert.Equal(Larkspur, Assert.Single(rows).Title);
    }

    [Theory]
    [InlineData(WatchSource.MarkedWatched)]
    [InlineData(WatchSource.MarkedUnwatched)]
    public void AMarkNewerThanTheRemovalIsNotASitting(WatchSource mark)
    {
        var rows = ContinueWatching.Build(
            [
                Episode(Lighthouse, E(1, 1), DaysAgo(5)),
                new WatchEvent(Lighthouse, WatchedKind.Show, DaysAgo(1), Service: null, E(1, 2), mark),
            ],
            [Running(Lighthouse, 6, DaysAgo(100))],
            [new ContinueDismissal(Lighthouse, DaysAgo(3))],
            Now);

        Assert.Empty(rows);
    }

    [Fact]
    public void ARemovalOnlyTakesItsOwnTitle()
    {
        var rows = ContinueWatching.Build(
            [Episode(Ferry, E(1, 1), DaysAgo(4))],
            [Running(Ferry, 6, DaysAgo(100))],
            [new ContinueDismissal(Lighthouse, DaysAgo(3))],
            Now);

        Assert.Equal([Ferry], rows.Select(row => row.Title));
    }

    /// <summary>Two removals of one title (the same record from two phones' eras): the later one stands.</summary>
    [Fact]
    public void TheLatestRemovalOfATitleIsTheOneThatCounts()
    {
        var rows = ContinueWatching.Build(
            [Episode(Lighthouse, E(1, 1), DaysAgo(5)), Episode(Lighthouse, E(1, 2), DaysAgo(2))],
            [Running(Lighthouse, 6, DaysAgo(100))],
            [new ContinueDismissal(Lighthouse, DaysAgo(3)), new ContinueDismissal(Lighthouse, DaysAgo(1))],
            Now);

        Assert.Empty(rows);
    }
}
