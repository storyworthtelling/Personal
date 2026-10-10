using Pulp.Domain.Core;
using Pulp.Domain.Library;
using static Pulp.Tests.Library.LibraryFixtures;

namespace Pulp.Tests.Library;

/// <summary>
/// The words on a Recently watched row (mockup 05, D227): a show's last episode and its whole-show
/// pencil mark until it is complete, then "Finished"; a film's ending — Finished, Stopped at N of M
/// min, or a mark by hand either way (D278) — then where it played. Invented titles and services.
/// </summary>
public class RecentRowTextTests
{
    private static RecentRow Film(FilmEnding ending, ProgressMark? progress = null, ServiceId? service = null) =>
        new(Larkspur, TitleKind.Film, Now, service ?? Streamly, LastEpisode: null, progress, ShowFinished: false, ending);

    private static RecentRow Show(EpisodeRef? last, ProgressMark? progress, bool finished = false) =>
        new(Lighthouse, TitleKind.Show, Now, Streamly, last, progress, finished, Ending: null);

    [Fact]
    public void AShowInProgressNamesItsLastEpisodeAndCountsTheWholeShow()
    {
        var lines = RecentRowText.For(Show(E(1, 2), new ProgressMark(2, 14, ProgressUnit.Episode)), "Slack Water", "Streamly");

        Assert.Equal("Last: S1 · E2 · Slack Water", lines.Subtitle);
        Assert.Equal("2 of 14 episodes · Streamly", lines.ProgressLine);
        Assert.Equal(new ProgressMark(2, 14, ProgressUnit.Episode), lines.Bar);
        Assert.Null(lines.Detail);
    }

    [Fact]
    public void AnEpisodeWithNoNameIsStillNamedBySeasonAndNumber()
    {
        var lines = RecentRowText.For(Show(E(2, 4), new ProgressMark(12, 18, ProgressUnit.Episode)), null, "Streamly");

        Assert.Equal("Last: S2 · E4", lines.Subtitle);
    }

    [Fact]
    public void AFinishedShowReadsFinishedAndDrawsNoMark()
    {
        var lines = RecentRowText.For(Show(E(3, 8), progress: null, finished: true), "Last Light", "Streamly");

        Assert.Equal("Finished · Streamly", lines.Detail);
        Assert.Null(lines.Bar);
        Assert.Null(lines.ProgressLine);
    }

    [Fact]
    public void AShowWhoseLengthIsUnknownSaysOnlyWhereItPlayed()
    {
        var lines = RecentRowText.For(Show(E(1, 1), progress: null), "Pilot", "Streamly");

        Assert.Equal("Streamly", lines.Detail);
        Assert.Null(lines.Bar);
    }

    [Fact]
    public void AFilmSaysHowItEndedThenWhereItPlayed()
    {
        Assert.Equal("Finished · Streamly", RecentRowText.For(Film(FilmEnding.Finished), null, "Streamly").Detail);
        Assert.Equal(
            "Stopped at 46 of 118 min · Kestrel TV",
            RecentRowText.For(Film(FilmEnding.Stopped, new ProgressMark(46, 118, ProgressUnit.Minute)), null, "Kestrel TV").Detail);
        Assert.Equal("Film", RecentRowText.For(Film(FilmEnding.Finished), null, "Streamly").Subtitle);
    }

    [Fact]
    public void AMarkByHandSaysSoWithoutAPlace()
    {
        Assert.Equal("Marked watched by you", RecentRowText.For(Film(FilmEnding.MarkedWatched), null, "Streamly").Detail);
        Assert.Equal("Marked unwatched by you", RecentRowText.For(Film(FilmEnding.MarkedUnwatched), null, "Streamly").Detail);
    }

    [Fact]
    public void AStoppedFilmWithNoKnownLengthStillSaysItStopped()
    {
        Assert.Equal("Stopped partway · Streamly", RecentRowText.For(Film(FilmEnding.Stopped), null, "Streamly").Detail);
    }

    [Fact]
    public void NoServiceLeavesOffThePlace()
    {
        Assert.Equal("Finished", RecentRowText.For(Film(FilmEnding.Finished), null, null).Detail);
        Assert.Equal("2 of 14 episodes", RecentRowText.For(Show(E(1, 2), new ProgressMark(2, 14, ProgressUnit.Episode)), null, null).ProgressLine);
    }
}
