using System.Net;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;
using Pulp.Infrastructure.Sports;

namespace Pulp.Tests.Infrastructure.Sports;

/// <summary>
/// The league-feed adapter for baseball (D208). The JSON is invented but has the real response shapes of
/// https://statsapi.mlb.com/api/v1/seasons, /teams, /schedule (with hydrate=broadcasts) and /standings —
/// including the score, record and winner fields the real responses carry, which the adapter must drop.
/// </summary>
public class MlbStatsSourceTests
{
    private static readonly LeagueId Diamond = new("diamond-invented");
    private static readonly DateOnly Today = new(2026, 7, 20);
    private static readonly SportsWindow Window = new(Today, 1, 1);

    private const string Seasons = """
    { "copyright": "invented", "seasons": [ {
        "seasonId": "2026", "hasWildcard": true,
        "preSeasonStartDate": "2026-01-01", "preSeasonEndDate": "2026-02-19",
        "seasonStartDate": "2026-02-20", "springStartDate": "2026-02-20", "springEndDate": "2026-03-24",
        "regularSeasonStartDate": "2026-03-25", "regularSeasonEndDate": "2026-09-27",
        "postSeasonStartDate": "2026-09-28", "postSeasonEndDate": "2026-10-31",
        "seasonEndDate": "2026-10-31", "offseasonStartDate": "2026-11-01", "offSeasonEndDate": "2026-12-31" } ] }
    """;

    private const string NextSeason = """
    { "seasons": [ { "seasonId": "2027", "springStartDate": "2027-02-19", "regularSeasonStartDate": "2027-03-24",
        "regularSeasonEndDate": "2027-09-26", "postSeasonEndDate": "2027-10-30", "seasonEndDate": "2027-10-30" } ] }
    """;

    private const string Teams = """
    { "copyright": "invented", "teams": [
      { "id": 9001, "name": "Marsh Herons", "teamName": "Herons", "abbreviation": "MHR", "season": 2026,
        "league": { "id": 103, "name": "Invented League A" }, "sport": { "id": 1, "name": "Invented Baseball" }, "active": true },
      { "id": 9002, "name": "Pine Lake Otters", "teamName": "Otters", "abbreviation": "PLO", "season": 2026,
        "league": { "id": 104, "name": "Invented League B" }, "sport": { "id": 1 }, "active": true } ] }
    """;

    private static string Game(int pk, string date, string type, string home, string away, string broadcasts = "[]") => $$"""
    { "gamePk": {{pk}}, "gameType": "{{type}}", "season": "2026", "gameDate": "{{date}}", "officialDate": "{{date[..10]}}",
      "status": { "abstractGameState": "Final", "detailedState": "Final" },
      "teams": {
        "away": { "team": { "id": {{away}}, "name": "x", "link": "/api/v1/teams/{{away}}" },
                  "leagueRecord": { "wins": 61, "losses": 33, "pct": ".649" }, "score": 987654, "isWinner": true },
        "home": { "team": { "id": {{home}}, "name": "x", "link": "/api/v1/teams/{{home}}" },
                  "leagueRecord": { "wins": 40, "losses": 55, "pct": ".421" }, "score": 123456, "isWinner": false } },
      "venue": { "id": 1, "name": "Invented Park" }, "isTie": false,
      "seriesDescription": "Regular Season", "broadcasts": {{broadcasts}} }
    """;

    private static string Day(string date, params string[] games) =>
        $$"""{ "totalGames": {{games.Length}}, "dates": [ { "date": "{{date}}", "games": [ {{string.Join(",", games)}} ] } ] }""";

    private const string NoGames = """{ "totalGames": 0, "dates": [] }""";

    private readonly RoutedHandler handler = new();
    private readonly LookupTally tally = new();

    private MlbStatsSource Source() => new(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) }, tally, Diamond, "Invented Baseball");

    private RoutedHandler Standard(string schedule)
    {
        handler.Route("/api/v1/seasons?sportId=1&season=2026", Seasons)
            .Route("/api/v1/teams", Teams)
            .Route("/api/v1/schedule?sportId=1&startDate", schedule)
            .Route("gameType=W", NoGames);
        return handler;
    }

    [Fact]
    public async Task AScheduleAsksForTheSeasonTheTeamsTheWindowAndTheFinal()
    {
        Standard(Day("2026-07-20", Game(1, "2026-07-20T23:05:00Z", "R", "9001", "9002")));

        await Source().GetScheduleAsync(Diamond, Window, CancellationToken.None);

        Assert.All(handler.Requests, request => Assert.Equal("statsapi.mlb.com", request.Uri.Host));
        Assert.Contains(handler.Requests, request => request.Uri.AbsolutePath == "/api/v1/seasons" && request.Query("season") == "2026" && request.Query("sportId") == "1");
        Assert.Contains(handler.Requests, request => request.Uri.AbsolutePath == "/api/v1/teams" && request.Query("sportId") == "1" && request.Query("season") == "2026");
        var schedule = Assert.Single(handler.Requests, request => request.Query("startDate") is not null);
        Assert.Equal("2026-07-19", schedule.Query("startDate"));
        Assert.Equal("2026-07-21", schedule.Query("endDate"));
        Assert.Equal("broadcasts", schedule.Query("hydrate"));
        Assert.Contains(handler.Requests, request => request.Query("gameType") == "W" && request.Query("season") == "2026");
        Assert.Equal(handler.Requests.Count, tally.Count(LookupSource.MlbStats));
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task AGameIsMappedToScheduleTeamsStageChannelAndStartAndNothingElse()
    {
        Standard(Day("2026-07-20", Game(
            5551, "2026-07-20T23:05:00Z", "R", "9001", "9002",
            """[ { "name": "Invented Regional", "type": "TV", "isNational": false }, { "name": "Invented Network", "type": "TV", "isNational": true }, { "name": "Invented Radio", "type": "AM", "isNational": true } ]""")));

        var schedule = await Source().GetScheduleAsync(Diamond, Window, CancellationToken.None);

        var game = Assert.Single(schedule.Games);
        Assert.Equal(new GameId("5551"), game.Id);
        Assert.Equal(new TeamId("9001"), game.Home.Id);
        Assert.Equal("Marsh Herons", game.Home.Name);
        Assert.Equal("Pine Lake Otters", game.Away.Name);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 23, 5, 0, TimeSpan.Zero), game.StartsAt);
        Assert.Equal(GameStage.RegularSeason, game.Stage);
        Assert.Equal("Invented Network", game.Channel);
        Assert.Equal(2, schedule.Teams.Count);
    }

    [Fact]
    public async Task NoScoreRecordOrWinnerFromTheWireReachesAnythingTheDomainHolds()
    {
        Standard(Day("2026-07-20", Game(5551, "2026-07-20T23:05:00Z", "R", "9001", "9002")));

        var schedule = await Source().GetScheduleAsync(Diamond, Window, CancellationToken.None);

        var everything = string.Join(" ", schedule.Games.Select(game => game.ToString())) + schedule.League + string.Join(" ", schedule.Teams);
        foreach (var leaked in new[] { "987654", "123456", "649", "421", "True", "Winner", "wins" })
        {
            Assert.DoesNotContain(leaked, everything);
        }
    }

    [Theory]
    [InlineData("S", GameStage.Preseason)]
    [InlineData("E", GameStage.Preseason)]
    [InlineData("R", GameStage.RegularSeason)]
    [InlineData("A", GameStage.RegularSeason)]
    [InlineData("F", GameStage.Playoffs)]
    [InlineData("D", GameStage.Playoffs)]
    [InlineData("L", GameStage.Playoffs)]
    [InlineData("W", GameStage.Championship)]
    [InlineData("Z", GameStage.Playoffs)]
    public async Task AGameTypeBecomesAStageAndAnUnknownOneIsHeldAsARevealingPlayoff(string gameType, GameStage expected)
    {
        Standard(Day("2026-07-20", Game(1, "2026-07-20T23:05:00Z", gameType, "9001", "9002")));

        var schedule = await Source().GetScheduleAsync(Diamond, Window, CancellationToken.None);

        Assert.Equal(expected, Assert.Single(schedule.Games).Stage);
    }

    [Fact]
    public async Task ANationalTvNetworkWinsAndWithoutOneTheFirstTvIsUsedAndWithoutAnyTheChannelIsEmpty()
    {
        Standard(Day("2026-07-20",
            Game(1, "2026-07-20T23:05:00Z", "R", "9001", "9002", """[ { "name": "Local One", "type": "TV", "isNational": false }, { "name": "Local Two", "type": "TV", "isNational": false } ]"""),
            Game(2, "2026-07-20T23:35:00Z", "R", "9001", "9002", """[ { "name": "Radio Only", "type": "AM", "isNational": false } ]"""),
            Game(3, "2026-07-20T23:45:00Z", "R", "9001", "9002")));

        var games = (await Source().GetScheduleAsync(Diamond, Window, CancellationToken.None)).Games;

        Assert.Equal(["Local One", string.Empty, string.Empty], games.Select(game => game.Channel));
    }

    [Theory]
    [InlineData("2026-03-01", SeasonPhase.Preseason, "2026-10-31")]
    [InlineData("2026-07-20", SeasonPhase.RegularSeason, "2026-10-31")]
    [InlineData("2026-10-10", SeasonPhase.Postseason, "2026-10-31")]
    public async Task TheSeasonDatesSayWhereTheLeagueIs(string today, SeasonPhase phase, string seasonEnds)
    {
        Standard(NoGames);
        var window = new SportsWindow(DateOnly.Parse(today), 0, 0);

        var league = (await Source().GetScheduleAsync(Diamond, window, CancellationToken.None)).League;

        Assert.Equal(phase, league.Phase);
        Assert.Equal(DateOnly.Parse(seasonEnds), league.SeasonEnds);
        Assert.Equal(Diamond, league.Id);
        Assert.Equal("Invented Baseball", league.Name);
    }

    [Fact]
    public async Task AfterTheSeasonTheLeagueIsOffSeasonWithTheNextSeasonsFirstDayAsItsReturn()
    {
        Standard(NoGames);
        handler.Route("/api/v1/seasons?sportId=1&season=2027", NextSeason);
        var window = new SportsWindow(new DateOnly(2026, 12, 1), 0, 0);

        var league = (await Source().GetScheduleAsync(Diamond, window, CancellationToken.None)).League;

        Assert.Equal(SeasonPhase.OffSeason, league.Phase);
        Assert.Equal(new DateOnly(2027, 3, 24), league.NextDate);
        Assert.Equal(new DateOnly(2027, 10, 30), league.SeasonEnds);
    }

    [Fact]
    public async Task TheChampionshipIsTheEarliestFinalGameWithItsTeamsAndTheSeriesName()
    {
        Standard(NoGames);
        handler.Route("gameType=W", Day("2026-10-24",
            Game(8002, "2026-10-26T00:08:00Z", "W", "9002", "9001"),
            Game(8001, "2026-10-25T00:08:00Z", "W", "9001", "9002")).Replace("\"seriesDescription\": \"Regular Season\"", "\"seriesDescription\": \"Invented Series\""));

        var championship = (await Source().GetScheduleAsync(Diamond, Window, CancellationToken.None)).Championship;

        Assert.NotNull(championship);
        Assert.Equal(new GameId("8001"), championship.Id);
        Assert.Equal("Invented Series", championship.Name);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 8, 0, TimeSpan.Zero), championship.StartsAt);
        Assert.Equal(["Marsh Herons", "Pine Lake Otters"], championship.Teams.Select(team => team.Name));
    }

    [Fact]
    public async Task NoFinalYetMeansNoChampionship()
    {
        Standard(NoGames);

        Assert.Null((await Source().GetScheduleAsync(Diamond, Window, CancellationToken.None)).Championship);
    }

    [Fact]
    public async Task StandingsAreTheTeamsInSportRankOrderAndNothingElse()
    {
        handler.Route("/api/v1/standings", """
        { "records": [
          { "standingsType": "regularSeason", "league": { "id": 103 }, "teamRecords": [
            { "team": { "id": 9002, "name": "Pine Lake Otters" }, "sportRank": "2", "leagueRank": "1", "wins": 61, "losses": 33, "runsScored": 777, "leagueRecord": { "wins": 61 } } ] },
          { "standingsType": "regularSeason", "league": { "id": 104 }, "teamRecords": [
            { "team": { "id": 9001, "name": "Marsh Herons" }, "sportRank": "1", "leagueRank": "1", "wins": 70, "losses": 24, "runsScored": 888 } ] } ] }
        """);

        var order = await Source().GetStandingsOrderAsync(Diamond, CancellationToken.None);

        Assert.Equal([new TeamId("9001"), new TeamId("9002")], order.Select(team => team.Id));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/standings", request.Uri.AbsolutePath);
        Assert.Equal("103,104", request.Query("leagueId"));
        Assert.DoesNotContain("777", string.Join(" ", order));
        Assert.Equal(handler.Requests.Count, tally.Count(LookupSource.MlbStats));
    }

    [Fact]
    public async Task ALeagueTheSourceWasNotAskedAboutIsRefusedBeforeAnythingIsSent()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Source().GetScheduleAsync(new LeagueId("elsewhere"), Window, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Source().GetStandingsOrderAsync(new LeagueId("elsewhere"), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AServerErrorIsAnUnavailableSourceNamingMlbStats()
    {
        handler.Route("/api/v1/seasons", "boom", HttpStatusCode.InternalServerError);

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Source().GetScheduleAsync(Diamond, Window, CancellationToken.None));

        Assert.Equal(LookupFailure.Unavailable, thrown.Failure);
        Assert.Equal(LookupSource.MlbStats, thrown.Source);
        Assert.Equal(500, thrown.StatusCode);
    }

    [Fact]
    public async Task ABodyThatIsNotTheDocumentedShapeIsMalformedAndSaysWhere()
    {
        handler.Route("/api/v1/seasons", """{ "seasons": [] }""");

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Source().GetScheduleAsync(Diamond, Window, CancellationToken.None));

        Assert.Equal(LookupFailure.Malformed, thrown.Failure);
        Assert.Contains("season", thrown.Message);
    }

    [Fact]
    public async Task TooManyRequestsIsThrottled()
    {
        handler.Route("/api/v1/seasons", "slow down", HttpStatusCode.TooManyRequests);

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Source().GetScheduleAsync(Diamond, Window, CancellationToken.None));

        Assert.Equal(LookupFailure.Throttled, thrown.Failure);
    }

    [Fact]
    public async Task ATimedOutSourceIsUnavailableAndTheCallersCancellationStillPropagates()
    {
        var silent = new HttpClient(new SilentHandler()) { Timeout = TimeSpan.FromMilliseconds(50) };
        var timedOut = await Assert.ThrowsAsync<CatalogueLookupException>(
            () => new MlbStatsSource(silent, tally, Diamond, "Invented Baseball").GetScheduleAsync(Diamond, Window, CancellationToken.None));
        Assert.Equal(LookupFailure.Unavailable, timedOut.Failure);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var patient = new HttpClient(new SilentHandler()) { Timeout = TimeSpan.FromMinutes(5) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new MlbStatsSource(patient, tally, Diamond, "Invented Baseball").GetScheduleAsync(Diamond, Window, cancellation.Token));
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
