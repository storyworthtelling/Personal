using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Friday;
using Pulp.Domain.Library;
using Pulp.Domain.People;

namespace Pulp.Tests.Discover;

/// <summary>
/// What the stack's viewer has watched (D282) and what taste learns from (D283). Whose stack it is
/// is <see cref="ThisPhone"/>'s answer, tested there. Invented people only.
/// </summary>
public class DiscoverPageWatchedTests
{
    private static readonly ViewerId Sam = new("adult-example-a");
    private static readonly ViewerId Ash = new("adult-example-b");
    private static readonly ViewerId Wren = new("child-example-wren");
    private static readonly DateTimeOffset Evening = new(2026, 10, 12, 20, 0, 0, TimeSpan.Zero);

    private static WatchEvent Film(string id, int minutesIn, bool ended, int dayOffset = 0, WatchSource source = WatchSource.Session) => new(
        new TitleId(id), WatchedKind.Film, Evening.AddDays(dayOffset), null, null, source,
        TimeSpan.FromMinutes(minutesIn), TimeSpan.FromMinutes(100), ended);

    private static WatchEvent Episode(string id, int season, int number, bool ended) => new(
        new TitleId(id), WatchedKind.Show, Evening, null, new EpisodeRef(season, number), WatchSource.Session,
        TimeSpan.FromMinutes(ended ? 30 : 5), TimeSpan.FromMinutes(30), ended);

    [Fact]
    public void AFinishedFilmIsWatchedAndOneStoppedShortIsNot()
    {
        var watched = DiscoverPageWatched.AlreadyWatched([Film("f-done", 100, true), Film("f-short", 10, false)]);

        Assert.Contains(new TitleId("f-done"), watched);
        Assert.DoesNotContain(new TitleId("f-short"), watched);
    }

    [Fact]
    public void AShowWithAnyEpisodeWatchedIsNoLongerNew()
    {
        var watched = DiscoverPageWatched.AlreadyWatched(
            [Episode("s-seen", 1, 1, ended: true), Episode("s-seen", 1, 2, ended: false), Episode("s-sampled", 1, 1, ended: false)]);

        Assert.Contains(new TitleId("s-seen"), watched);
        Assert.DoesNotContain(new TitleId("s-sampled"), watched);
    }

    [Fact]
    public void MarkedUnwatchedAfterwardsCountsAsUnwatched()
    {
        var watched = DiscoverPageWatched.AlreadyWatched(
            [Film("f-again", 100, true), Film("f-again", 0, false, dayOffset: 1, source: WatchSource.MarkedUnwatched), Film("f-kept", 100, true)]);

        Assert.Equal([new TitleId("f-kept")], watched.ToArray());
    }

    [Fact]
    public void WhatTasteLearnsFromNamesEveryViewerWhoWatchedSoAChildsSittingIsLeftOut()
    {
        var sittings = new Dictionary<ViewerId, IReadOnlyList<WatchEvent>>
        {
            [Sam] = [Film("f-family", 100, true), Film("f-alone", 100, true)],
            [Wren] = [Film("f-family", 100, true)],
            [Ash] = [Film("f-short", 5, false)],
        };

        var titles = DiscoverPageWatched.WatchedTitles(sittings, id => id.Value == "f-alone" ? ["noir"] : ["cartoon"]);
        var roster = new HouseholdRoster([new HouseholdViewer(Sam, false), new HouseholdViewer(Ash, false), new HouseholdViewer(Wren, true)]);
        var taste = TasteLearner.Learn(Sam, roster, titles, VetoBook.For(Sam));

        Assert.Equal(2, titles.Count);
        Assert.Equal([Sam, Wren], titles.Single(t => t.Title.Value == "f-family").Viewers.OrderBy(v => v.Value).ToArray());
        Assert.Equal(1, taste.Affinity(["noir"]));
        Assert.Equal(0, taste.Affinity(["cartoon"]));
    }
}
