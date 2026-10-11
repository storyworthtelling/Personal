using System.Net;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;
using Pulp.Infrastructure.Sports;

namespace Pulp.Tests.Infrastructure.Sports;

/// <summary>
/// The adapter for the leagues without a league feed of their own (D208). The JSON is invented but has the
/// real shapes of site.api.espn.com's scoreboard, teams and standings responses (measured live: a scoreboard
/// is <c>leagues[].season</c> plus <c>events[].competitions[].competitors[]</c>), including the score, winner,
/// record and leader fields the real responses carry, which the adapter must drop.
/// </summary>
public class EspnSourceTests
{
    private static readonly LeagueId Gridball = new("cfl-invented");
    private static readonly EspnLeague Definition = new(Gridball, "Coastal Football League", "gridball", "cfl", ["Coastal Bowl"]);
    private const string SitePath = "/apis/site/v2/sports/gridball/cfl";
    private static readonly DateOnly Today = new(2026, 1, 20);
    private static readonly SportsWindow Window = new(Today, 1, 1);

    private const string Teams = """
    { "sports": [ { "id": "1", "leagues": [ { "id": "9", "name": "Invented", "teams": [
      { "team": { "id": "11", "displayName": "Harbor Gulls", "logos": [ { "href": "https://example.com/gulls.png" } ] } },
      { "team": { "id": "12", "displayName": "Ridge Foxes", "logos": [ { "href": "https://example.com/foxes.png" } ] } },
      { "team": { "id": "13", "displayName": "Bay Herons", "logos": [ { "href": "https://example.com/herons.png" } ] } } ] } ] } ] }
    """;

    private static string Competitor(string side, string id, string name) => $$"""
    { "id": "{{id}}", "type": "team", "homeAway": "{{side}}", "winner": {{(side == "home" ? "true" : "false")}},
      "team": { "id": "{{id}}", "displayName": "{{name}}", "abbreviation": "XXX", "logo": "https://example.com/{{id}}.png" },
      "score": "{{(side == "home" ? "31337" : "27182")}}", "linescores": [ { "value": 7.0 } ],
      "records": [ { "name": "overall", "summary": "13-4" } ] }
    """;

    private static string Event(
        string id, string date, int seasonType, string headline = "", string broadcasts = "[ { \"market\": \"national\", \"names\": [ \"Invented Network\", \"Invented Stream\" ] } ]", bool bothTeams = true)
    {
        var notes = headline.Length == 0 ? "[]" : $$"""[ { "type": "event", "headline": "{{headline}}" } ]""";
        var competitors = bothTeams
            ? $"{Competitor("home", "11", "Harbor Gulls")}, {Competitor("away", "12", "Ridge Foxes")}"
            : string.Empty;
        return $$"""
        { "id": "{{id}}", "date": "{{date}}", "name": "x", "season": { "year": 2025, "type": {{seasonType}} },
          "competitions": [ { "id": "{{id}}", "competitors": [ {{competitors}} ], "notes": {{notes}}, "broadcasts": {{broadcasts}},
            "status": { "type": { "completed": true } }, "leaders": [ { "name": "passingYards", "displayValue": "28/35, 273 YDS" } ] } ],
          "status": { "type": { "completed": true } } }
        """;
    }

    private static string Board(string seasonStart, string seasonEnd, int seasonType, params string[] events) => $$"""
    { "leagues": [ { "id": "9", "name": "Invented", "logos": [ { "href": "https://example.com/league.png", "rel": ["full", "default"] } ],
        "season": { "year": 2025, "startDate": "{{seasonStart}}T07:00Z", "endDate": "{{seasonEnd}}T07:59Z", "type": { "id": "{{seasonType}}", "type": {{seasonType}} } } } ],
      "events": [ {{string.Join(",", events)}} ] }
    """;

    private readonly RoutedHandler handler = new();
    private readonly LookupTally tally = new();

    private EspnSource Source() => new(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) }, [Definition], tally);

    private RoutedHandler Standard(string board)
    {
        handler.Route("/teams", Teams).Route("/scoreboard", board);
        return handler;
    }

    [Fact]
    public async Task AScheduleAsksForTheTeamsAndOneScoreboardPerDay()
    {
        Standard(Board("2025-08-01", "2026-02-12", 2, Event("e1", "2026-01-20T18:00Z", 2)));

        await Source().GetScheduleAsync(Gridball, Window, CancellationToken.None);

        Assert.All(handler.Requests, request => Assert.Equal("site.api.espn.com", request.Uri.Host));
        Assert.Contains(handler.Requests, request => request.Uri.AbsolutePath == SitePath + "/teams");
        var days = handler.Requests.Where(request => request.Uri.AbsolutePath == SitePath + "/scoreboard").ToList();
        Assert.Equal(["20260119", "20260120", "20260121"], days.Select(request => request.Query("dates")).Order());
        Assert.All(days, request => Assert.Equal("300", request.Query("limit")));
        Assert.Equal(4, tally.Count(LookupSource.Espn));
    }

    [Fact]
    public async Task AGameIsMappedToTeamsStageChannelAndStartAndNothingElse()
    {
        Standard(Board("2025-08-01", "2026-02-12", 2, Event("e1", "2026-01-20T18:00Z", 2)));

        var schedule = await Source().GetScheduleAsync(Gridball, new SportsWindow(Today, 0, 0), CancellationToken.None);

        var game = Assert.Single(schedule.Games);
        Assert.Equal(new GameId("e1"), game.Id);
        Assert.Equal(new TeamId("espn:cfl-invented:11"), game.Home.Id);
        Assert.Equal("Harbor Gulls", game.Home.Name);
        Assert.Equal("https://example.com/gulls.png", game.Home.BadgeArt);
        Assert.Equal("Ridge Foxes", game.Away.Name);
        Assert.Equal(new DateTimeOffset(2026, 1, 20, 18, 0, 0, TimeSpan.Zero), game.StartsAt);
        Assert.Equal(GameStage.RegularSeason, game.Stage);
        Assert.Equal("Invented Network · Invented Stream", game.Channel);
        Assert.Equal(["Harbor Gulls", "Ridge Foxes", "Bay Herons"], schedule.Teams.Select(team => team.Name));
        Assert.Equal("https://example.com/gulls.png", schedule.Teams[0].BadgeArt);
        Assert.Equal("https://example.com/league.png", schedule.League.BadgeArt);
        Assert.Equal("Coastal Football League", schedule.League.Name);
    }

    [Fact]
    public async Task NoScoreWinnerRecordOrLeaderFromTheWireReachesAnythingTheDomainHolds()
    {
        Standard(Board("2025-08-01", "2026-02-12", 2, Event("e1", "2026-01-20T18:00Z", 2)));

        var schedule = await Source().GetScheduleAsync(Gridball, new SportsWindow(Today, 0, 0), CancellationToken.None);

        var everything = string.Join(" ", schedule.Games.Select(game => game.ToString())) + schedule.League + string.Join(" ", schedule.Teams);
        foreach (var leaked in new[] { "31337", "27182", "13-4", "273", "True", "passingYards" })
        {
            Assert.DoesNotContain(leaked, everything);
        }
    }

    [Theory]
    [InlineData(1, GameStage.Preseason)]
    [InlineData(2, GameStage.RegularSeason)]
    [InlineData(3, GameStage.Playoffs)]
    [InlineData(4, GameStage.Playoffs)]
    [InlineData(9, GameStage.Playoffs)]
    public async Task ASeasonTypeBecomesAStageAndAnUnknownOneIsHeldAsARevealingPlayoff(int seasonType, GameStage expected)
    {
        Standard(Board("2025-08-01", "2026-02-12", 2, Event("e1", "2026-01-20T18:00Z", seasonType)));

        var schedule = await Source().GetScheduleAsync(Gridball, new SportsWindow(Today, 0, 0), CancellationToken.None);

        Assert.Equal(expected, Assert.Single(schedule.Games).Stage);
    }

    [Fact]
    public async Task APostseasonGameWhoseNoteNamesTheChampionshipIsTheChampionshipAndLeavesNoDuplicate()
    {
        Standard(Board("2025-08-01", "2026-02-12", 3,
            Event("e1", "2026-01-20T18:00Z", 3, "Conference Round"),
            Event("e9", "2026-01-21T23:30Z", 3, "The Coastal Bowl LX"),
            Event("e8", "2026-01-22T23:30Z", 3, "The Coastal Bowl LX")));

        var schedule = await Source().GetScheduleAsync(Gridball, new SportsWindow(Today, 0, 2), CancellationToken.None);

        Assert.NotNull(schedule.Championship);
        Assert.Equal(new GameId("e9"), schedule.Championship.Id);
        Assert.Equal("Coastal Bowl", schedule.Championship.Name);
        Assert.Equal(["Harbor Gulls", "Ridge Foxes"], schedule.Championship.Teams.Select(team => team.Name));
        Assert.Equal(GameStage.Playoffs, schedule.Games.Single(game => game.Id == new GameId("e1")).Stage);
        Assert.Equal(GameStage.Championship, schedule.Games.Single(game => game.Id == new GameId("e9")).Stage);
    }

    private static readonly EspnLeague Hoops = new(new LeagueId("hoops-invented"), "Hoops League", "hoops", "bbl", ["Hoops Final"]);

    [Fact]
    public async Task ATeamIdNamesItsLeagueSoTwoLeaguesSharingARawIdAreTwoTeams()
    {
        handler.Route("/gridball/cfl/teams", Teams).Route("/hoops/bbl/teams", Teams)
            .Route("/gridball/cfl/scoreboard", Board("2025-08-01", "2026-02-12", 2, Event("e1", "2026-01-20T18:00Z", 2)))
            .Route("/hoops/bbl/scoreboard", Board("2025-08-01", "2026-02-12", 2, Event("e2", "2026-01-20T18:00Z", 2)));
        var source = new EspnSource(new HttpClient(handler), [Definition, Hoops], tally);
        var window = new SportsWindow(Today, 0, 0);

        var football = await source.GetScheduleAsync(Gridball, window, CancellationToken.None);
        var hoops = await source.GetScheduleAsync(Hoops.Id, window, CancellationToken.None);

        // The feed's raw id "11" is the same in both; the Domain's id is not.
        Assert.NotEqual(football.Teams[0].Id, hoops.Teams[0].Id);
        Assert.Equal(new TeamId("espn:hoops-invented:11"), hoops.Games[0].Home.Id);

        var feeds = new[] { LeagueFeeds.Assemble(football, [], []), LeagueFeeds.Assemble(hoops, [], []) };
        var follows = new[] { new SportsFollow(new Pulp.Domain.Core.ViewerId("robin-example"), football.Teams[0].Id) };
        Assert.Equal([Gridball], SportsFollowing.FollowedLeagues(feeds, follows, new Pulp.Domain.Core.ViewerId("robin-example")));
        Assert.Contains(SportsFollowing.Fixtures(feeds, follows, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)), fixture => fixture.League == Gridball);
        Assert.DoesNotContain(SportsFollowing.Fixtures(feeds, follows, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)), fixture => fixture.League == Hoops.Id);
    }

    [Fact]
    public async Task TheChampionshipIsNamedByTheLeaguesMarkerNeverByTheFeedsHeadline()
    {
        Standard(Board("2025-08-01", "2026-02-12", 3,
            Event("e9", "2026-01-21T23:30Z", 3, "Coastal Bowl LX - Game 6 (Harbor Gulls lead 3-2)")));

        var schedule = await Source().GetScheduleAsync(Gridball, new SportsWindow(Today, 0, 2), CancellationToken.None);

        Assert.Equal("Coastal Bowl", schedule.Championship!.Name);
        Assert.DoesNotContain("lead", schedule.Championship.Name);
        Assert.DoesNotContain("Gulls", schedule.Championship.Name);
    }

    [Fact]
    public async Task TheChampionshipIsTheFirstFinalGameStillToCome()
    {
        Standard(Board("2025-08-01", "2026-02-12", 3,
            Event("past", "2026-01-12T23:30Z", 3, "Coastal Bowl"),
            Event("today", "2026-01-20T23:30Z", 3, "Coastal Bowl"),
            Event("later", "2026-01-22T23:30Z", 3, "Coastal Bowl")));

        var schedule = await Source().GetScheduleAsync(Gridball, new SportsWindow(Today, 8, 2), CancellationToken.None);

        Assert.Equal(new GameId("today"), schedule.Championship!.Id);
    }

    [Fact]
    public async Task WithEveryFinalGameBehindItTheLatestIsTheChampionship()
    {
        Standard(Board("2025-08-01", "2026-02-12", 3,
            Event("first", "2026-01-10T23:30Z", 3, "Coastal Bowl"),
            Event("last", "2026-01-12T23:30Z", 3, "Coastal Bowl")));

        var schedule = await Source().GetScheduleAsync(Gridball, new SportsWindow(Today, 10, 0), CancellationToken.None);

        Assert.Equal(new GameId("last"), schedule.Championship!.Id);
    }

    [Fact]
    public async Task AnEventListedOnTwoDaysIsOneGame()
    {
        Standard(Board("2025-08-01", "2026-02-12", 2, Event("e1", "2026-01-20T18:00Z", 2)));

        var schedule = await Source().GetScheduleAsync(Gridball, Window, CancellationToken.None);

        Assert.Single(schedule.Games);
    }

    [Fact]
    public async Task AnEventWithoutBothTeamsIsNotAGame()
    {
        Standard(Board("2025-08-01", "2026-02-12", 2, Event("allstar", "2026-01-20T18:00Z", 2, bothTeams: false), Event("e1", "2026-01-20T20:00Z", 2)));

        var schedule = await Source().GetScheduleAsync(Gridball, new SportsWindow(Today, 0, 0), CancellationToken.None);

        Assert.Equal([new GameId("e1")], schedule.Games.Select(game => game.Id));
    }

    [Fact]
    public async Task WithoutANationalBroadcastTheFirstOneIsUsedAndWithoutAnyTheChannelIsEmpty()
    {
        Standard(Board("2025-08-01", "2026-02-12", 2,
            Event("e1", "2026-01-20T18:00Z", 2, broadcasts: """[ { "market": "away", "names": [ "Away Regional" ] }, { "market": "home", "names": [ "Home Regional" ] } ]"""),
            Event("e2", "2026-01-20T19:00Z", 2, broadcasts: "[]")));

        var games = (await Source().GetScheduleAsync(Gridball, new SportsWindow(Today, 0, 0), CancellationToken.None)).Games;

        Assert.Equal(["Away Regional", string.Empty], games.Select(game => game.Channel));
    }

    [Fact]
    public async Task ALeagueMidSeasonIsInSeasonByItsNearestGameAndEndsOnTheSeasonsLastDay()
    {
        Standard(Board("2025-08-01", "2026-02-12", 2, Event("e1", "2026-01-20T18:00Z", 3, "Conference Round")));

        var league = (await Source().GetScheduleAsync(Gridball, new SportsWindow(Today, 0, 0), CancellationToken.None)).League;

        Assert.Equal(SeasonPhase.Postseason, league.Phase);
        Assert.Equal(new DateOnly(2026, 2, 12), league.SeasonEnds);
    }

    [Fact]
    public async Task AQuietWeekInsideTheSeasonFallsBackToTheSeasonTypeTheFeedStates()
    {
        Standard(Board("2025-08-01", "2026-02-12", 2));

        var league = (await Source().GetScheduleAsync(Gridball, Window, CancellationToken.None)).League;

        Assert.Equal(SeasonPhase.RegularSeason, league.Phase);
    }

    [Fact]
    public async Task AfterTheSeasonTheLeagueIsOffSeason()
    {
        Standard(Board("2025-08-01", "2026-02-12", 2));

        var league = (await Source().GetScheduleAsync(Gridball, new SportsWindow(new DateOnly(2026, 4, 1), 0, 0), CancellationToken.None)).League;

        Assert.Equal(SeasonPhase.OffSeason, league.Phase);
        Assert.Null(league.NextDate);
    }

    [Fact]
    public async Task BeforeTheNextSeasonTheLeagueIsOffSeasonWithItsReturnDate()
    {
        Standard(Board("2026-09-15", "2027-02-12", 2));

        var league = (await Source().GetScheduleAsync(Gridball, Window, CancellationToken.None)).League;

        Assert.Equal(SeasonPhase.OffSeason, league.Phase);
        Assert.Equal(new DateOnly(2026, 9, 15), league.NextDate);
        Assert.Equal(new DateOnly(2027, 2, 12), league.SeasonEnds);
    }

    [Fact]
    public async Task PreseasonGamesBeforeTheSeasonMakeItPreseasonWithTheNextGameAsItsDate()
    {
        Standard(Board("2026-09-15", "2027-02-12", 2, Event("e1", "2026-01-21T18:00Z", 1)));

        var league = (await Source().GetScheduleAsync(Gridball, Window, CancellationToken.None)).League;

        Assert.Equal(SeasonPhase.Preseason, league.Phase);
        Assert.Equal(new DateOnly(2026, 1, 21), league.NextDate);
    }

    [Fact]
    public async Task StandingsAreTheTeamsInRankOrderAcrossGroupsAndNothingElse()
    {
        handler.Route("/standings", """
        { "name": "Invented", "children": [
          { "name": "East", "standings": { "entries": [
            { "team": { "id": "13", "displayName": "Bay Herons" }, "stats": [ { "name": "wins", "value": 12.0 }, { "name": "points", "value": 9999.0 }, { "name": "playoffSeed", "value": 2.0 } ] },
            { "team": { "id": "11", "displayName": "Harbor Gulls" }, "stats": [ { "name": "points", "value": 8888.0 }, { "name": "playoffSeed", "value": 1.0 } ] } ] } },
          { "name": "West", "standings": { "entries": [
            { "team": { "id": "12", "displayName": "Ridge Foxes" }, "stats": [ { "name": "playoffSeed", "value": 1.0 } ] } ] } } ] }
        """);

        var order = await Source().GetStandingsOrderAsync(Gridball, CancellationToken.None);

        Assert.Equal(["espn:cfl-invented:11", "espn:cfl-invented:12", "espn:cfl-invented:13"], order.Select(team => team.Id.Value));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/apis/v2/sports/gridball/cfl" + "/standings", request.Uri.AbsolutePath);
        Assert.DoesNotContain("9999", string.Join(" ", order));
        Assert.Equal(1, tally.Count(LookupSource.Espn));
    }

    [Fact]
    public async Task AFlatStandingsTableIsReadByRankAndATeamWithNoRankComesLast()
    {
        handler.Route("/standings", """
        { "name": "Invented", "standings": { "entries": [
          { "team": { "id": "13", "displayName": "Bay Herons" }, "stats": [ { "name": "points", "value": 5.0 } ] },
          { "team": { "id": "12", "displayName": "Ridge Foxes" }, "stats": [ { "name": "rank", "value": 2.0 } ] },
          { "team": { "id": "11", "displayName": "Harbor Gulls" }, "stats": [ { "name": "rank", "value": 1.0 } ] } ] } }
        """);

        var order = await Source().GetStandingsOrderAsync(Gridball, CancellationToken.None);

        Assert.Equal(["espn:cfl-invented:11", "espn:cfl-invented:12", "espn:cfl-invented:13"], order.Select(team => team.Id.Value));
    }

    [Fact]
    public async Task ALeagueNotGivenToTheAdapterIsRefusedBeforeAnythingIsSent()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Source().GetScheduleAsync(new LeagueId("elsewhere"), Window, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Source().GetStandingsOrderAsync(new LeagueId("elsewhere"), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ABadRequestAnswerIsAnUnavailableSourceNamingEspn()
    {
        handler.Route("/teams", """{ "code": 400, "message": "Failed to get events endpoint." }""", HttpStatusCode.BadRequest);

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Source().GetScheduleAsync(Gridball, Window, CancellationToken.None));

        Assert.Equal(LookupFailure.Unavailable, thrown.Failure);
        Assert.Equal(LookupSource.Espn, thrown.Source);
        Assert.Equal(400, thrown.StatusCode);
    }

    [Fact]
    public async Task ABodyThatIsNotTheDocumentedShapeIsMalformed()
    {
        handler.Route("/teams", """{ "sports": [] }""");

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Source().GetScheduleAsync(Gridball, Window, CancellationToken.None));

        Assert.Equal(LookupFailure.Malformed, thrown.Failure);
        Assert.Contains("teams", thrown.Message);
    }

    [Fact]
    public async Task ATimedOutSourceIsUnavailableAndTheCallersCancellationStillPropagates()
    {
        var silent = new HttpClient(new SilentHandler()) { Timeout = TimeSpan.FromMilliseconds(50) };
        var timedOut = await Assert.ThrowsAsync<CatalogueLookupException>(
            () => new EspnSource(silent, [Definition], tally).GetStandingsOrderAsync(Gridball, CancellationToken.None));
        Assert.Equal(LookupFailure.Unavailable, timedOut.Failure);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var patient = new HttpClient(new SilentHandler()) { Timeout = TimeSpan.FromMinutes(5) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new EspnSource(patient, [Definition], tally).GetStandingsOrderAsync(Gridball, cancellation.Token));
    }

    [Fact]
    public void TheShippedLeaguesAreExactlyTheTablesEspnLeagues()
    {
        var table = SportsSourceTable.Default;

        var fromTable = table.Leagues.Where(entry => entry.SourceId == SportsSourceIds.Espn).Select(entry => entry.League).OrderBy(id => id.Value);
        Assert.Equal(fromTable, EspnLeagues.Default.Select(league => league.Id).OrderBy(id => id.Value));
        Assert.All(EspnLeagues.Default, league => Assert.NotEmpty(league.ChampionshipMarkers));
        Assert.Contains(table.Leagues, entry => entry.League == MlbStatsSource.DefaultLeague && entry.SourceId == SportsSourceIds.MlbStats);
    }

    private sealed class SilentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The silent handler returned without being cancelled.");
        }
    }
}
