using Pulp.Domain.Actions;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Title;
using static Pulp.Tests.Title.TitleFixtures;

namespace Pulp.Tests.Title;

/// <summary>
/// Which episodes take the pine tick, which one Play names, which season is open (D159, D217), and
/// what a long-press mark writes (D160). Invented titles only.
///
/// <para>The bugs these reintroduce: the title page and Continue Watching disagreeing on what is next
/// (two rules for one question), a page that opens on Season 1 when the viewer is halfway through
/// Season 2, "Watched up to here" re-marking episodes already watched (or skipping the one pressed),
/// another viewer's sittings ticking this viewer's episodes, and a mark the undo cannot find.</para>
/// </summary>
public class TitleProgressTests
{
    [Fact]
    public void WatchedEpisodesAreTheOnesTheWatchedRuleSaysSo()
    {
        var sittings = new[]
        {
            Watched(E(1, 1), Now.AddDays(-10)),
            Watched(E(1, 2), Now.AddDays(-9)),
            new WatchEvent(Saltmarsh, WatchedKind.Show, Now.AddDays(-8), Northstar, E(1, 3),
                StoppedAt: TimeSpan.FromMinutes(5), Runtime: TimeSpan.FromMinutes(50)),
        };

        Assert.Equal([E(1, 1), E(1, 2)], TitleProgress.WatchedEpisodes(Saltmarsh, sittings).Order());
    }

    [Fact]
    public void AnotherTitlesSittingsTickNothingHere()
    {
        var other = new WatchEvent(new TitleId("ferry-show-example"), WatchedKind.Show, Now.AddDays(-2), Northstar, E(1, 1), Ended: true);

        Assert.Empty(TitleProgress.WatchedEpisodes(Saltmarsh, [other]));
    }

    [Fact]
    public void NextIsWhatContinueWatchingWouldPlay()
    {
        var sittings = new[] { Watched(E(1, 1), Now.AddDays(-10)), Watched(E(1, 2), Now.AddDays(-9)) };

        Assert.Equal(E(1, 3), TitleProgress.NextUp(Show(), sittings, Now));
    }

    [Fact]
    public void NextSkipsPastTheFurthestWatchedEvenWithAGapBehindIt()
    {
        var sittings = new[] { Watched(E(1, 1), Now.AddDays(-10)), Watched(E(1, 4), Now.AddDays(-9)) };

        Assert.Equal(E(1, 5), TitleProgress.NextUp(Show(), sittings, Now));
    }

    [Fact]
    public void ANeverWatchedShowStartsAtTheFirstEpisode()
    {
        Assert.Equal(E(1, 1), TitleProgress.NextUp(Show(), [], Now));
    }

    [Fact]
    public void AnEpisodeThatHasNotAiredIsNeverNext()
    {
        // Season 2 began a month ago, one a week: episodes 1–5 have aired, 6–8 have not.
        var sittings = Enumerable.Range(1, 6).Select(n => Watched(E(1, n), Now.AddDays(-60 + n)))
            .Concat(Enumerable.Range(1, 5).Select(n => Watched(E(2, n), Now.AddDays(-20 + n))))
            .ToList();

        Assert.Null(TitleProgress.NextUp(Show(), sittings, Now));
    }

    [Fact]
    public void WithNothingLeftPlayStartsTheShowAgain()
    {
        var sittings = Enumerable.Range(1, 6).Select(n => Watched(E(1, n), Now.AddDays(-60 + n)))
            .Concat(Enumerable.Range(1, 5).Select(n => Watched(E(2, n), Now.AddDays(-20 + n))))
            .ToList();

        Assert.Equal(E(1, 1), TitleProgress.ToPlay(Show(), sittings, Now));
    }

    [Fact]
    public void AFilmHasNoEpisodeToPlay()
    {
        Assert.Null(TitleProgress.ToPlay(Film(), [], Now));
        Assert.Null(TitleProgress.NextUp(Film(), [], Now));
    }

    [Fact]
    public void TheOpenSeasonIsTheNextEpisodesSeason()
    {
        var sittings = Enumerable.Range(1, 6).Select(n => Watched(E(1, n), Now.AddDays(-60 + n))).ToList();

        Assert.Equal(2, TitleProgress.OpenSeason(Show(), TitleProgress.NextUp(Show(), sittings, Now), TitleProgress.WatchedEpisodes(Saltmarsh, sittings)));
    }

    [Fact]
    public void WithNothingNextTheOpenSeasonIsTheLastOneWatched()
    {
        Assert.Equal(2, TitleProgress.OpenSeason(Show(), null, new HashSet<EpisodeRef> { E(1, 2), E(2, 4) }));
        Assert.Equal(1, TitleProgress.OpenSeason(Show(), null, new HashSet<EpisodeRef>()));
    }

    [Fact]
    public void MarkingOneEpisodeWatchedWritesOneMark()
    {
        var marks = EpisodeMarks.For(CardActionKind.Watched, Show(), E(1, 3), new HashSet<EpisodeRef>(), Now);

        var mark = Assert.Single(marks);
        Assert.Equal(new WatchEvent(Saltmarsh, WatchedKind.Show, Now, null, E(1, 3), WatchSource.MarkedWatched), mark);
    }

    [Fact]
    public void MarkingUnwatchedWritesAnUnwatchedMark()
    {
        var mark = Assert.Single(EpisodeMarks.For(CardActionKind.Unwatched, Show(), E(1, 2), new HashSet<EpisodeRef> { E(1, 2) }, Now));

        Assert.Equal(WatchSource.MarkedUnwatched, mark.Evidence);
        Assert.Equal(E(1, 2), mark.Episode);
    }

    [Fact]
    public void WatchedUpToHereMarksEveryEarlierUnwatchedEpisodeAndThisOne()
    {
        var watched = new HashSet<EpisodeRef> { E(1, 2) };

        var marks = EpisodeMarks.For(CardActionKind.WatchedUpToHere, Show(), E(2, 1), watched, Now);

        Assert.Equal([E(1, 1), E(1, 3), E(1, 4), E(1, 5), E(1, 6), E(2, 1)], marks.Select(mark => mark.Episode!));
        Assert.All(marks, mark => Assert.Equal(WatchSource.MarkedWatched, mark.Evidence));
    }

    [Fact]
    public void AnEpisodeCardOffersNoOtherMark()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EpisodeMarks.For(CardActionKind.RemoveFromHistory, Show(), E(1, 1), new HashSet<EpisodeRef>(), Now));
    }

    [Fact]
    public void AMarkOnAnEpisodeTheShowDoesNotListIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            EpisodeMarks.For(CardActionKind.Watched, Show(), E(9, 1), new HashSet<EpisodeRef>(), Now));
    }
}
