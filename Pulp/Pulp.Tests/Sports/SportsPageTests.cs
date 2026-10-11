using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// What the Sports tab is handed: the held or open sheet of each league the viewer follows, in league
/// order, and the fold — never a raw feed (G15's other end). The assembly is Domain so the head stays thin.
/// </summary>
public class SportsPageTests
{
    private static readonly ViewerId Robin = new("robin-example");

    private static LeagueFeed Southern2() => new(
        new League(Southern, "Southern Rules League", "badge://srl", SeasonPhase.RegularSeason, new DateOnly(2026, 9, 1), null),
        [Otters],
        [new Game(new GameId("s-1"), Otters, Gulls, Now.AddDays(2), GameStage.RegularSeason, "Gridline TV")],
        [Otters],
        [new Highlight("video-two", "Round up")],
        null);

    private static WatchEvent Watched(GameId game) =>
        new(new TitleId(game.Value), WatchedKind.Game, Now.AddDays(-1), null, Evidence: WatchSource.MarkedWatched);

    private static SportsPage Build(
        IEnumerable<LeagueFeed> feeds, IEnumerable<TeamId> follows, IEnumerable<WatchEvent>? sittings = null, SpoilerPeeks? peeks = null) =>
        SportsPage.Build(feeds, follows.Select(team => new SportsFollow(Robin, team)).ToList(), Robin, sittings ?? [], Now, peeks ?? new SpoilerPeeks(), FoldMemory.Empty);

    [Fact]
    public void ALeagueWithAnUnwatchedRecordingComesBackHeldWithNothingToRevealIt()
    {
        var page = Build([CoastalFeed()], [Gulls.Id]);

        var held = Assert.IsType<HeldLeague>(Assert.Single(page.Sheets));
        Assert.True(held.Teams.Single().Games.Single().Recorded);
    }

    [Fact]
    public void WatchingTheRecordingOpensTheLeagueWithStandingsAndHighlights()
    {
        var page = Build([CoastalFeed()], [Gulls.Id], [Watched(GullsRecording)]);

        var open = Assert.IsType<OpenLeague>(Assert.Single(page.Sheets));
        Assert.False(open.IsPeek);
        Assert.NotEmpty(open.Standings);
        Assert.NotEmpty(open.Highlights);
    }

    [Fact]
    public void APeekOpensAHeldLeagueOnceAndTheHoldComesBack()
    {
        var peeks = new SpoilerPeeks();
        peeks.Tap(Coastal);

        var first = Build([CoastalFeed()], [Gulls.Id], peeks: peeks);
        var second = Build([CoastalFeed()], [Gulls.Id], peeks: peeks);

        Assert.True(Assert.IsType<OpenLeague>(Assert.Single(first.Sheets)).IsPeek);
        Assert.IsType<HeldLeague>(Assert.Single(second.Sheets));
    }

    [Fact]
    public void OnlyLeaguesWithAFollowedTeamAppearAndTheyComeInLeagueOrder()
    {
        var offSeason = CoastalFeed() with { League = CoastalLeague() with { Phase = SeasonPhase.OffSeason, NextDate = new DateOnly(2026, 8, 1) } };

        var page = Build([offSeason, Southern2()], [Gulls.Id, Otters.Id]);

        Assert.Equal([Southern, Coastal], page.Sheets.Select(Line));
        var unfollowed = Build([CoastalFeed(), Southern2()], [Foxes.Id]);
        Assert.Equal([Coastal], unfollowed.Sheets.Select(Line));
    }

    [Fact]
    public void NobodyFollowedMeansNoSheets()
    {
        Assert.Empty(Build([CoastalFeed()], []).Sheets);
    }

    [Fact]
    public void TheFoldStartsOnTheLeagueClosestToTheEndOfItsSeason()
    {
        var page = Build([CoastalFeed(), Southern2()], [Gulls.Id, Otters.Id]);

        Assert.True(page.Fold.IsLeagueOpen(Coastal));
        Assert.False(page.Fold.IsLeagueOpen(Southern));
    }

    [Fact]
    public void ATeamAFollowerNamesThatNoFeedHasIsUnlocated()
    {
        var stranger = new TeamId("stranger-example");

        var unlocated = SportsPage.Unlocated([CoastalFeed()], [new SportsFollow(Robin, Gulls.Id), new SportsFollow(Robin, stranger)]);

        Assert.Equal([stranger], unlocated);
        Assert.Empty(SportsPage.Unlocated([CoastalFeed()], [new SportsFollow(Robin, Gulls.Id)]));
    }

    private static LeagueId Line(LeagueSheet sheet) => sheet switch
    {
        HeldLeague held => held.Line.Id,
        OpenLeague open => open.Line.Id,
        _ => throw new InvalidOperationException("A sheet is held or open."),
    };
}
