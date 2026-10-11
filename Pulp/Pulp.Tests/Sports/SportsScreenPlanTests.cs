using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// What the Sports screen draws, as data (mockup 07): leagues in order, each a foldable line with its
/// teams and games, the championship at league level, the one Spoilers tag. The plan is built from the
/// sheets <see cref="SportsPage"/> hands over and from nothing else, so a held league has no standing,
/// bracket or highlight to put on the page.
/// </summary>
public class SportsScreenPlanTests
{
    private static readonly ViewerId Robin = new("robin-example");
    private static readonly TimeZoneInfo Zone = SportsScreenTextTests.Zone;

    private static readonly Team Pelicans = new(new TeamId("dune-pelicans"), "Dune Pelicans", "badge://dune-pelicans");
    private static readonly Team Wolves = new(new TeamId("lake-wolves"), "Lake Wolves", "badge://lake-wolves");

    private static LeagueFeed SouthernPreseason() => new(
        new League(Southern, "Southern Rules League", "badge://srl", SeasonPhase.Preseason, new DateOnly(2026, 9, 1), new DateOnly(2026, 1, 23)),
        [Pelicans],
        [new Game(new GameId("s-1"), Pelicans, Otters, Now.AddDays(3), GameStage.Preseason, "Gridline TV")],
        [Pelicans],
        [],
        null);

    private static LeagueFeed LakeshoreOffSeason() => new(
        new League(Lakeshore, "Lakeshore Women's Hockey", "badge://lwh", SeasonPhase.OffSeason, new DateOnly(2026, 3, 1), new DateOnly(2026, 11, 8)),
        [Wolves],
        [],
        [Wolves],
        [],
        null);

    private static SportsPage Page(
        IEnumerable<LeagueFeed> feeds, IEnumerable<Team> follows, IEnumerable<WatchEvent>? sittings = null, SpoilerPeeks? peeks = null) =>
        SportsPage.Build(
            feeds, follows.Select(team => new SportsFollow(Robin, team.Id)).ToList(), Robin, sittings ?? [], Now,
            peeks ?? new SpoilerPeeks(), FoldMemory.Empty);

    private static WatchEvent Watched(GameId game) =>
        new(new TitleId(game.Value), WatchedKind.Game, Now.AddDays(-1), null, Evidence: WatchSource.MarkedWatched);

    private static IReadOnlyList<LeaguePlan> Plan(SportsPage page, FoldMemory? overlay = null) =>
        SportsScreenPlan.For(page, overlay ?? FoldMemory.Empty, Now, Zone);

    private static SportsPage MockupPage() =>
        Page([LakeshoreOffSeason(), SouthernPreseason(), CoastalFeed()], [Gulls, Pelicans, Wolves]);

    // ---- the page as mockup 07 draws it ----

    [Fact]
    public void LeaguesComeInLeagueOrderWithTheSeasonsEndFirst()
    {
        var plan = Plan(MockupPage());

        Assert.Equal(["Coastal Football League", "Southern Rules League", "Lakeshore Women's Hockey"], plan.Select(league => league.Name));
    }

    [Fact]
    public void OnlyTheLeagueClosestToItsEndStartsOpenAndTheRestAreOneLine()
    {
        var plan = Plan(MockupPage());

        Assert.Equal([true, false, false], plan.Select(league => league.Open));
    }

    [Fact]
    public void AFoldedLeagueNamesItsStateAndAnOpenOneDoesNot()
    {
        var plan = Plan(MockupPage());

        Assert.Null(plan[0].LineNote);
        Assert.Equal("Preseason · next Fri", plan[1].LineNote);
        Assert.Equal("Back Nov 8", plan[2].LineNote);
    }

    [Fact]
    public void TheSpoilersTagIsOnTheHeldLeagueAlone()
    {
        var plan = Plan(MockupPage());

        Assert.Equal([true, false, false], plan.Select(league => league.SpoilersTag));
    }

    [Fact]
    public void TheHeldLeaguesTeamOpensToItsRecordingAndTheRecordingIsTheOnlyGame()
    {
        var coastal = Plan(MockupPage())[0];

        var gulls = Assert.Single(coastal.Teams);
        Assert.True(gulls.Open);
        Assert.Equal("Harbor Gulls", gulls.Name);
        var game = Assert.Single(gulls.Games);
        Assert.Equal("Harbor Gulls at Ridge Foxes", game.Title);
        Assert.Equal("Sunday · recorded · from the start", game.Detail);
        Assert.Equal("On Gridline TV · channel 7", game.ChannelLine);
        Assert.Equal("Playoffs", game.Chip);
        Assert.True(game.Recorded);
    }

    [Fact]
    public void TheChampionshipSitsAtLeagueLevelWithItsDateAndNoTeamsWhileHeld()
    {
        var champ = Plan(MockupPage())[0].Championship;

        Assert.NotNull(champ);
        Assert.Equal("The Coastal Bowl", champ!.Name);
        Assert.Equal("Championship · Sun Feb 8", champ.Detail);
        Assert.Equal("Final", champ.Chip);
        Assert.Null(champ.TeamsLine);
    }

    [Fact]
    public void AHeldLeagueOffersNoStandingsBracketScheduleOrHighlights()
    {
        var coastal = Plan(MockupPage())[0];

        Assert.Empty(coastal.Links);
        Assert.Empty(coastal.Highlights);
    }

    // ---- the hold lifting, and the peek ----

    [Fact]
    public void WatchingTheRecordingOpensTheLeaguesPagesAndHighlightsAndNamesTheFinalists()
    {
        var plan = Plan(Page([CoastalFeed()], [Gulls], [Watched(GullsRecording)]));

        var coastal = Assert.Single(plan);
        Assert.False(coastal.SpoilersTag);
        Assert.Equal([SportsLeaguePage.Standings, SportsLeaguePage.Bracket, SportsLeaguePage.Schedule], coastal.Links.Select(link => link.Kind));
        Assert.Equal(["Standings", "Playoff bracket", "Schedule"], coastal.Links.Select(link => link.Label));
        Assert.Equal("Week in review", Assert.Single(coastal.Highlights).Title);
        Assert.Equal("Bay Herons vs Harbor Gulls", coastal.Championship!.TeamsLine);
    }

    [Fact]
    public void ThePeekShowsThePagesAndKeepsTheTag()
    {
        var peeks = new SpoilerPeeks();
        peeks.Tap(Coastal);

        var coastal = Assert.Single(Plan(Page([CoastalFeed()], [Gulls], peeks: peeks)));

        Assert.True(coastal.SpoilersTag);
        Assert.NotEmpty(coastal.Links);
    }

    [Fact]
    public void WithNoTeamFollowedThereAreNoLeagues()
    {
        Assert.Empty(Plan(Page([CoastalFeed()], [])));
    }

    // ---- folds ----

    [Fact]
    public void WhatTheViewerOpensOrFoldsWinsOverTheStartingRule()
    {
        var page = MockupPage();
        var overlay = FoldMemory.Empty.WithLeague(Southern, true).WithLeague(Coastal, false);

        var plan = Plan(page, overlay);

        Assert.Equal([false, true, false], plan.Select(league => league.Open));
    }

    [Fact]
    public void ATeamsFoldIsRememberedWithinItsLeague()
    {
        var page = MockupPage();

        var folded = Plan(page, FoldMemory.Empty.WithTeam(Coastal, Gulls.Id, false))[0].Teams.Single();
        var untouched = Plan(page)[0].Teams.Single();

        Assert.False(folded.Open);
        Assert.True(untouched.Open);
    }

    [Fact]
    public void AFoldedLeagueStillCarriesItsTeamsSoOpeningItCostsNoRead()
    {
        var southern = Plan(MockupPage())[1];

        Assert.False(southern.Open);
        Assert.Equal("Dune Pelicans", Assert.Single(southern.Teams).Name);
        Assert.False(southern.Teams[0].Open);
    }

    // ---- order ----

    [Fact]
    public void TeamsWithARecordingComeFirstThenByName()
    {
        var teams = new List<TeamGames>
        {
            new(Otters, []),
            new(Gulls, [new GameLine(new GameId("a"), Foxes, Gulls, Now.AddDays(-1), GameStage.RegularSeason, "x", true)]),
            new(Foxes, []),
        };

        var ordered = SportsScreenOrder.Teams(teams);

        Assert.Equal(["Harbor Gulls", "Cove Otters", "Ridge Foxes"], ordered.Select(team => team.Team.Name));
    }

    [Fact]
    public void GamesRunRecordingsOldestFirstThenTheNextGame()
    {
        var older = new GameLine(new GameId("old"), Foxes, Gulls, Now.AddDays(-3), GameStage.RegularSeason, "x", true);
        var newer = new GameLine(new GameId("new"), Foxes, Gulls, Now.AddDays(-1), GameStage.RegularSeason, "x", true);
        var next = new GameLine(new GameId("next"), Foxes, Gulls, Now.AddDays(2), GameStage.RegularSeason, "x", false);

        var ordered = SportsScreenOrder.Games([next, newer, older]);

        Assert.Equal(["old", "new", "next"], ordered.Select(game => game.Id.Value));
    }

    [Fact]
    public void ATeamWithNothingScheduledSaysSoInsteadOfOpeningOntoNothing()
    {
        var wolves = Plan(MockupPage())[2].Teams.Single();

        Assert.Empty(wolves.Games);
        Assert.Equal("Nothing scheduled.", wolves.EmptyLine);
    }

    // ---- the pages behind an open league (no mockup, D272) ----

    private static OpenLeague OpenCoastal(params Team[] follows) =>
        Assert.IsType<OpenLeague>(Assert.Single(Page([CoastalFeed()], follows, [Watched(GullsRecording)]).Sheets));

    [Fact]
    public void StandingsAreRankOrderWithTheFollowedTeamsMarked()
    {
        var rows = SportsScreenPages.Standings(OpenCoastal(Gulls));

        Assert.Equal(["Bay Herons", "Harbor Gulls", "Ridge Foxes", "Cove Otters"], rows.Select(row => row.Name));
        Assert.Equal([1, 2, 3, 4], rows.Select(row => row.Rank));
        Assert.Equal([false, true, false, false], rows.Select(row => row.Followed));
    }

    [Fact]
    public void TheBracketPageHoldsTheChampionshipAndThePlayoffGames()
    {
        var bracket = SportsScreenPages.Bracket(OpenCoastal(Gulls), Now, Zone);

        Assert.Equal("The Coastal Bowl", bracket.Championship!.Name);
        var game = Assert.Single(bracket.Playoffs);
        Assert.Equal("Harbor Gulls at Bay Herons", game.Title);
        Assert.Equal("Playoffs", game.Chip);
    }

    [Fact]
    public void TheSchedulePageListsEachGameOnceInTimeOrderWhoeverFollowsIt()
    {
        var meeting = new Game(new GameId("g-meet"), Foxes, Gulls, Now.AddDays(1), GameStage.RegularSeason, "Gridline TV");
        var feed = CoastalFeed() with { Schedule = [meeting] };
        var page = Page([feed], [Gulls, Foxes]);
        var open = Assert.IsType<OpenLeague>(Assert.Single(page.Sheets));

        var rows = SportsScreenPages.Schedule(open, Now, Zone);

        Assert.Equal("Harbor Gulls at Ridge Foxes", Assert.Single(rows).Title);
    }

    [Fact]
    public void NoPageBuilderTakesAHeldLeagueOrAnythingRawer()
    {
        var parameters = typeof(SportsScreenPages).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        Assert.Contains(typeof(OpenLeague), parameters);
        Assert.DoesNotContain(typeof(HeldLeague), parameters);
        Assert.DoesNotContain(typeof(LeagueSheet), parameters);
        Assert.DoesNotContain(typeof(LeagueFeed), parameters);
    }

    [Fact]
    public void AGameRowIsBuiltFromASheetsGameLineAndNeverFromAFeedsGame()
    {
        var constructors = typeof(GameRow).GetConstructors().SelectMany(constructor => constructor.GetParameters());
        var factories = typeof(GameRow).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .SelectMany(method => method.GetParameters());
        var types = constructors.Concat(factories).Select(parameter => parameter.ParameterType).ToList();

        Assert.Contains(typeof(GameLine), types);
        Assert.DoesNotContain(typeof(Game), types);
        Assert.DoesNotContain(typeof(LeagueFeed), types);
    }
}
