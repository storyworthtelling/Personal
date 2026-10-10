using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using static Pulp.Tests.Library.LibraryFixtures;

namespace Pulp.Tests.Library;

/// <summary>
/// What "Mark watched" on a playlist row writes (mockup 10's playlist entries, D160, D278): a mark by
/// hand that <see cref="PlaylistPageStanding"/> then reads as watched, so the row strikes. A film takes
/// one whole-title mark; a show takes one per aired episode, which is what its standing counts —
/// a whole-show mark would leave the row unstruck. A show whose episodes are unknown, a game or a
/// franchise gets nothing, rather than a mark the page cannot read back.
/// </summary>
public class PlaylistPageMarksTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static CatalogueTitle EndedShow() => new()
    {
        Id = Lighthouse,
        Kind = TitleKind.Show,
        Name = "The Lighthouse Keepers",
        Ended = true,
        Seasons =
        [
            new CatalogueSeason(1, "Season 1",
            [
                new CatalogueEpisode(E(1, 1), "One", string.Empty, null, null, DaysAgo(20), []),
                new CatalogueEpisode(E(1, 2), "Two", string.Empty, null, null, DaysAgo(13), []),
            ]),
        ],
    };

    [Fact]
    public void AFilmTakesOneMarkAndStrikes()
    {
        var entry = PlaylistEntry.ForTitle(Larkspur, TitleKind.Film);

        var marks = PlaylistPageMarks.Watch(entry, title: null, Now);

        var mark = Assert.Single(marks);
        Assert.Equal(WatchSource.MarkedWatched, mark.Evidence);
        Assert.True(PlaylistPageStanding.For(entry, marks, null, Now, Utc).Watched);
    }

    [Fact]
    public void AnEndedShowTakesAMarkPerAiredEpisodeAndStrikes()
    {
        var entry = PlaylistEntry.ForTitle(Lighthouse, TitleKind.Show);
        var show = EndedShow();

        var marks = PlaylistPageMarks.Watch(entry, show, Now);

        Assert.Equal([E(1, 1), E(1, 2)], marks.Select(mark => mark.Episode));
        Assert.True(PlaylistPageStanding.For(entry, marks, show, Now, Utc).Watched);
    }

    [Fact]
    public void AShowWithNoKnownEpisodesIsNotMarked()
    {
        Assert.Empty(PlaylistPageMarks.Watch(PlaylistEntry.ForTitle(Lighthouse, TitleKind.Show), title: null, Now));
    }

    [Fact]
    public void AFranchiseIsNotMarked()
    {
        Assert.Empty(PlaylistPageMarks.Watch(new PlaylistEntry(PlaylistEntryKind.Franchise, "franchise-example"), title: null, Now));
    }
}
