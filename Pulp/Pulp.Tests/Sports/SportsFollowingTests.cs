using Pulp.Domain.Core;
using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// D60 (follows are per adult, every followed game goes on the household calendar labelled with who
/// follows it), D239 (a followed league's championship is always followed) and D240 (only followed
/// leagues' championships).
/// </summary>
public class SportsFollowingTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");

    private static LeagueFeed SouthernFeed() => new(
        new League(Southern, "Southern Rules League", "badge://srl", SeasonPhase.RegularSeason, new DateOnly(2026, 9, 1), null),
        [Otters],
        [new Game(new GameId("s-1"), Otters, Otters, Now.AddDays(2), GameStage.RegularSeason, "Pitchside")],
        [],
        [],
        new Championship(new GameId("s-grand"), "The Southern Grand Final", Now.AddDays(200), []));

    [Fact]
    public void ALeagueIsFollowedWhenAnyOfItsTeamsIs()
    {
        var follows = new[] { new SportsFollow(Sam, Gulls.Id) };

        var leagues = SportsFollowing.FollowedLeagues([CoastalFeed(), SouthernFeed()], follows, Sam);

        Assert.Equal([Coastal], leagues);
    }

    [Fact]
    public void FixturesAreTheFollowedTeamsUpcomingGamesWithTheirFollowers()
    {
        var follows = new[] { new SportsFollow(Sam, Gulls.Id), new SportsFollow(Alex, Gulls.Id) };

        var fixtures = SportsFollowing.Fixtures([CoastalFeed()], follows, Now);

        var next = Assert.Single(fixtures, fixture => fixture.Id == GullsNextPlayoff);
        Assert.Equal([Alex, Sam], next.Followers.OrderBy(viewer => viewer.Value));
        Assert.Equal("Harbor Gulls at Bay Herons", next.Name);
        Assert.DoesNotContain(fixtures, fixture => fixture.Id == HeronsNextPlayoff);
        Assert.DoesNotContain(fixtures, fixture => fixture.Id == GullsRecording);
    }

    [Fact]
    public void AFollowedLeaguesChampionshipIsAlwaysFollowedEvenWithoutAFollowedTeamInIt()
    {
        var feed = CoastalFeed() with
        {
            Championship = new Championship(CoastalBowl, "The Coastal Bowl", Now.AddDays(19), [Herons, Foxes]),
        };

        var fixtures = SportsFollowing.Fixtures([feed], [new SportsFollow(Sam, Gulls.Id)], Now);

        var bowl = Assert.Single(fixtures, fixture => fixture.Id == CoastalBowl);
        Assert.Equal(GameStage.Championship, bowl.Stage);
        Assert.Equal("The Coastal Bowl", bowl.Name);
        Assert.Equal([Sam], bowl.Followers);
    }

    [Fact]
    public void AnUnfollowedLeaguesChampionshipIsNotFollowed()
    {
        var fixtures = SportsFollowing.Fixtures(
            [CoastalFeed(), SouthernFeed()], [new SportsFollow(Sam, Gulls.Id)], Now);

        Assert.DoesNotContain(fixtures, fixture => fixture.League == Southern);
    }
}
