using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Watching;
using static Pulp.Tests.Library.LibraryFixtures;

namespace Pulp.Tests.Library;

/// <summary>
/// One definition of "watched" (D278, D279). The Library's lists and <see cref="Watched.Decide"/> are
/// fed the same evidence — once as the Watching rule's own shapes, once as the Library's sittings —
/// and must agree. Each case is one the two used to disagree on silently. Runtimes are invented.
/// </summary>
public class WatchedAgreementTests
{
    private static readonly TimeSpan Runtime = TimeSpan.FromMinutes(100);

    private static readonly IReadOnlyDictionary<TitleId, int> NoCounts = new Dictionary<TitleId, int>();

    private static WatchedVerdict Decide(IEnumerable<SessionObservation> sessions, IEnumerable<ManualMark>? marks = null) =>
        Watched.Decide(new WatchEvidence([], sessions, marks ?? []));

    private static WatchEvent FilmAt(DateTimeOffset at, TimeSpan stoppedAt, TimeSpan? runtime) =>
        new(Tidewater, WatchedKind.Film, at, Streamly, StoppedAt: stoppedAt, Runtime: runtime);

    private static RecentRow RecentFilm(IEnumerable<WatchEvent> events) =>
        RecentlyWatched.Build(events, NoCounts, TimeZoneInfo.Utc).SelectMany(day => day.Rows).Single(row => row.Title == Tidewater);

    [Fact]
    public void AFilmStoppedInTheClosingCreditsIsWatchedByBothPaths()
    {
        // (a) 97 of 100 minutes: past D279's 95%, so the rule says watched and the lists must too.
        var at = DaysAgo(1);
        var verdict = Decide([new SessionObservation(at, TimeSpan.FromMinutes(97), Runtime, ended: false)]);
        WatchEvent[] events = [FilmAt(at, TimeSpan.FromMinutes(97), Runtime)];

        Assert.True(verdict.IsWatched);
        Assert.DoesNotContain(ContinueWatching.Build(events, [], Now), row => row.Title == Tidewater);
        Assert.Equal(FilmEnding.Finished, RecentFilm(events).Ending);
    }

    [Fact]
    public void AnEpisodeStoppedInTheClosingCreditsMovesNextUpOnByBothPaths()
    {
        // (a) for a show: E1 stopped at 97% is watched, so next up is E2, not E1 again.
        var at = DaysAgo(1);
        var verdict = Decide([new SessionObservation(at, TimeSpan.FromMinutes(97), Runtime, ended: false)]);
        var schedule = new ShowSchedule(Ferry, Season(1, 4, DaysAgo(100)).ToList(), Ended: false);
        WatchEvent[] events = [new(Ferry, WatchedKind.Show, at, Streamly, E(1, 1), StoppedAt: TimeSpan.FromMinutes(97), Runtime: Runtime)];

        Assert.True(verdict.IsWatched);
        Assert.Equal(E(1, 2), Assert.Single(ContinueWatching.Build(events, [schedule], Now)).NextUp);
    }

    [Fact]
    public void APositionWithNoKnownRuntimeIsNotWatchedByBothPaths()
    {
        // (b) D279: an unknown duration never reaches the end, so the film stays on Continue Watching
        // and Recently Watched says it stopped rather than finished.
        var at = DaysAgo(1);
        var verdict = Decide([new SessionObservation(at, TimeSpan.FromMinutes(80), TimeSpan.Zero, ended: false)]);
        WatchEvent[] events = [FilmAt(at, TimeSpan.FromMinutes(80), runtime: null)];

        Assert.False(verdict.IsWatched);
        var row = Assert.Single(ContinueWatching.Build(events, [], Now));
        Assert.Equal(Tidewater, row.Title);
        Assert.Null(row.Progress);
        Assert.Equal(FilmEnding.Stopped, RecentFilm(events).Ending);
    }

    [Fact]
    public void AFilmMarkedUnwatchedAfterFinishingIsUnwatchedByBothPaths()
    {
        // (c) D278: the latest mark beats the finished sitting before it. Marking unwatched to see it
        // again puts the film back on Continue Watching and Recently Watched says it was marked so.
        var finished = DaysAgo(3);
        var marked = DaysAgo(1);
        var verdict = Decide(
            [new SessionObservation(finished, Runtime, Runtime, ended: false)],
            [new ManualMark(marked, Watched: false)]);
        WatchEvent[] events =
        [
            FilmAt(finished, Runtime, Runtime),
            new(Tidewater, WatchedKind.Film, marked, null, Evidence: WatchSource.MarkedUnwatched),
        ];

        Assert.False(verdict.IsWatched);
        Assert.Equal(WatchedReason.MarkedUnwatched, verdict.Reason);
        Assert.Equal(Tidewater, Assert.Single(ContinueWatching.Build(events, [], Now)).Title);
        Assert.Equal(FilmEnding.MarkedUnwatched, RecentFilm(events).Ending);
    }

    [Fact]
    public void AnEpisodeMarkedUnwatchedIsNextUpAgainByBothPaths()
    {
        // (c) for a show: both episodes seen to the end, then E2 marked unwatched — E2 is left.
        var schedule = new ShowSchedule(Ferry, Season(1, 2, DaysAgo(100)).ToList(), Ended: true);
        var verdict = Decide(
            [new SessionObservation(DaysAgo(4), Runtime, Runtime, ended: false)],
            [new ManualMark(DaysAgo(2), Watched: false)]);
        WatchEvent[] events =
        [
            new(Ferry, WatchedKind.Show, DaysAgo(5), Streamly, E(1, 1), StoppedAt: Runtime, Runtime: Runtime),
            new(Ferry, WatchedKind.Show, DaysAgo(4), Streamly, E(1, 2), StoppedAt: Runtime, Runtime: Runtime),
            new(Ferry, WatchedKind.Show, DaysAgo(2), null, E(1, 2), Evidence: WatchSource.MarkedUnwatched),
        ];

        Assert.False(verdict.IsWatched);
        Assert.Equal(E(1, 2), Assert.Single(ContinueWatching.Build(events, [schedule], Now)).NextUp);
        var recent = RecentlyWatched.Build(events, new Dictionary<TitleId, int> { [Ferry] = 2 }, TimeZoneInfo.Utc)
            .SelectMany(day => day.Rows).Single();
        Assert.False(recent.ShowFinished);
        Assert.Equal(new ProgressMark(1, 2, ProgressUnit.Episode), recent.Progress);
    }

    [Fact]
    public void MarkingUnwatchedIsNotASittingInRecentlyWatched()
    {
        // The row stays under the day the film was last seen, not the day it was marked; a title with
        // nothing but an unwatched mark was never watched and is not listed (D226 keeps sittings).
        var finished = DaysAgo(3);
        WatchEvent[] events =
        [
            FilmAt(finished, Runtime, Runtime),
            new(Tidewater, WatchedKind.Film, DaysAgo(1), null, Evidence: WatchSource.MarkedUnwatched),
            new(Larkspur, WatchedKind.Film, DaysAgo(1), null, Evidence: WatchSource.MarkedUnwatched),
        ];

        var rows = RecentlyWatched.Build(events, NoCounts, TimeZoneInfo.Utc).SelectMany(day => day.Rows).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(Tidewater, row.Title);
        Assert.Equal(finished, row.LastWatchedAt);
    }

    [Fact]
    public void WatchingAgainAfterMarkingUnwatchedIsLearnedByBothPaths()
    {
        // D278's second half: evidence newer than the mark speaks again.
        var verdict = Decide(
            [new SessionObservation(DaysAgo(5), Runtime, Runtime, ended: false), new SessionObservation(DaysAgo(1), Runtime, Runtime, ended: false)],
            [new ManualMark(DaysAgo(3), Watched: false)]);
        WatchEvent[] events =
        [
            FilmAt(DaysAgo(5), Runtime, Runtime),
            new(Tidewater, WatchedKind.Film, DaysAgo(3), null, Evidence: WatchSource.MarkedUnwatched),
            FilmAt(DaysAgo(1), Runtime, Runtime),
        ];

        Assert.True(verdict.IsWatched);
        Assert.Empty(ContinueWatching.Build(events, [], Now));
        Assert.Equal(FilmEnding.Finished, RecentFilm(events).Ending);
    }

    [Fact]
    public void ASittingCarriesNoVerdictOfItsOwn()
    {
        // The second definition lived on the record as a computed flag; the verdict is Watched.Decide's alone.
        // Every member of a sitting is something observed, given at construction; none is worked out.
        var given = typeof(WatchEvent).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        var properties = typeof(WatchEvent).GetProperties().Where(property => property.Name != "EqualityContract").ToList();

        Assert.NotEmpty(properties);
        Assert.All(properties, property => Assert.Contains(property.Name, given));
    }
}
