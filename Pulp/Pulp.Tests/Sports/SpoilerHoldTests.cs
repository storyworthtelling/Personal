using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// D70, D237, D242, D183, D236, D239: a league is held while a followed team has an unwatched
/// recording; held means standings, highlights and revealing fixtures are simply absent; the
/// Spoilers tag peeks once and the hold comes back.
/// </summary>
public class SpoilerHoldTests
{
    private static readonly TeamId[] FollowsGulls = [Gulls.Id];

    [Fact]
    public void AFollowedTeamWithAnUnwatchedRecordingHoldsItsLeague()
    {
        Assert.True(SpoilerHold.IsHeld(CoastalFeed(), FollowsGulls, [GullsRecording], Now));
    }

    [Fact]
    public void AWatchedRecordingReleasesTheHold()
    {
        Assert.False(SpoilerHold.IsHeld(CoastalFeed(), FollowsGulls, [], Now));
    }

    [Fact]
    public void AnUnfollowedTeamsRecordingDoesNotHoldTheLeague()
    {
        // The household recorded the Gulls game for someone else; this viewer follows the Otters.
        Assert.False(SpoilerHold.IsHeld(CoastalFeed(), [Otters.Id], [GullsRecording], Now));
    }

    [Fact]
    public void AGameNotYetPlayedIsNotAnUnwatchedRecording()
    {
        Assert.False(SpoilerHold.IsHeld(CoastalFeed(), FollowsGulls, [GullsNextPlayoff], Now));
    }

    [Fact]
    public void AHeldLeagueShowsTheRecordingButNotTheFixtureItsResultDecides()
    {
        var sheet = SpoilerHold.Present(CoastalFeed(), FollowsGulls, [GullsRecording], Now, peeking: false);

        var held = Assert.IsType<HeldLeague>(sheet);
        var gulls = Assert.Single(held.Teams);
        var game = Assert.Single(gulls.Games);
        Assert.Equal(GullsRecording, game.Id);
        Assert.True(game.Recorded);
    }

    [Fact]
    public void AHeldLeagueHidesEveryRevealingFixtureNotOnlyTheBehindTeams()
    {
        var sheet = SpoilerHold.Present(
            CoastalFeed(), [Gulls.Id, Herons.Id], [GullsRecording], Now, peeking: false);

        var held = Assert.IsType<HeldLeague>(sheet);
        var herons = held.Teams.Single(team => team.Team.Id == Herons.Id);
        Assert.Empty(herons.Games);
    }

    [Fact]
    public void AHeldChampionshipKeepsItsNameAndDateAndNamesNoTeams()
    {
        var held = Assert.IsType<HeldLeague>(
            SpoilerHold.Present(CoastalFeed(), FollowsGulls, [GullsRecording], Now, peeking: false));

        Assert.NotNull(held.Championship);
        Assert.Equal("The Coastal Bowl", held.Championship.Name);
        Assert.Equal(Now.AddDays(19), held.Championship.StartsAt);
    }

    [Fact]
    public void AnOpenLeagueCarriesStandingsHighlightsAndTheNextGame()
    {
        var sheet = SpoilerHold.Present(CoastalFeed(), FollowsGulls, [], Now, peeking: false);

        var open = Assert.IsType<OpenLeague>(sheet);
        Assert.False(open.IsPeek);
        Assert.Equal([Herons, Gulls, Foxes, Otters], open.Standings);
        Assert.Single(open.Highlights);
        Assert.Equal(GullsNextPlayoff, Assert.Single(Assert.Single(open.Teams).Games).Id);
        Assert.Equal([Herons, Gulls], open.Championship!.Teams);
    }

    [Fact]
    public void PerTeamTheUnwatchedRecordingsComeFirstThenTheNextGame()
    {
        // Regular season: nothing is revealing, so the held league still shows the next game.
        var feed = CoastalFeed(SeasonPhase.RegularSeason) with
        {
            Schedule =
            [
                new Game(new GameId("r-3"), Gulls, Foxes, Now.AddDays(6), GameStage.RegularSeason, "Gridline TV"),
                new Game(new GameId("r-2"), Foxes, Gulls, Now.AddDays(-1), GameStage.RegularSeason, "Gridline TV"),
                new Game(new GameId("r-1"), Gulls, Otters, Now.AddDays(-8), GameStage.RegularSeason, "Gridline TV"),
                new Game(new GameId("r-4"), Otters, Gulls, Now.AddDays(13), GameStage.RegularSeason, "Gridline TV"),
            ],
        };

        var held = Assert.IsType<HeldLeague>(SpoilerHold.Present(
            feed, FollowsGulls, [new GameId("r-1"), new GameId("r-2")], Now, peeking: false));

        Assert.Equal(["r-1", "r-2", "r-3"], held.Teams.Single().Games.Select(game => game.Id.Value));
    }

    [Fact]
    public void ALaterPlayoffRecordingIsHeldBehindTheEarlierOne()
    {
        // Two unwatched playoff recordings: the second exists only because the first was won.
        var feed = CoastalFeed() with
        {
            Schedule =
            [
                new Game(new GameId("p-1"), Foxes, Gulls, Now.AddDays(-6), GameStage.Playoffs, "Gridline TV"),
                new Game(new GameId("p-2"), Herons, Gulls, Now.AddDays(-1), GameStage.Playoffs, "Gridline TV"),
            ],
        };

        var held = Assert.IsType<HeldLeague>(SpoilerHold.Present(
            feed, FollowsGulls, [new GameId("p-1"), new GameId("p-2")], Now, peeking: false));

        Assert.Equal(["p-1"], held.Teams.Single().Games.Select(game => game.Id.Value));
    }

    [Fact]
    public void APeekOpensAHeldLeagueAndSaysSo()
    {
        var sheet = SpoilerHold.Present(CoastalFeed(), FollowsGulls, [GullsRecording], Now, peeking: true);

        var open = Assert.IsType<OpenLeague>(sheet);
        Assert.True(open.IsPeek);
        Assert.NotEmpty(open.Standings);
    }

    [Fact]
    public void ThePeekIsSpentOnceAndTheHoldReturns()
    {
        var peeks = new SpoilerPeeks();
        peeks.Tap(Coastal);

        Assert.True(peeks.Spend(Coastal));
        Assert.False(peeks.Spend(Coastal));
    }

    [Fact]
    public void APeekAtOneLeagueDoesNotOpenAnother()
    {
        var peeks = new SpoilerPeeks();
        peeks.Tap(Coastal);

        Assert.False(peeks.Spend(Southern));
        Assert.True(peeks.Spend(Coastal));
    }

    [Theory]
    [InlineData(GameStage.Playoffs, true)]
    [InlineData(GameStage.Championship, true)]
    [InlineData(GameStage.RegularSeason, false)]
    [InlineData(GameStage.Preseason, false)]
    public void PlayoffFixturesAreTheRevealingOnes(GameStage stage, bool reveals)
    {
        Assert.Equal(reveals, SpoilerHold.Reveals(stage));
    }
}
