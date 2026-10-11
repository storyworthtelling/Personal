using Pulp.Domain.Core;
using Pulp.Domain.Sports;
using Pulp.Infrastructure.Sports;
using Pulp.Tests.Infrastructure.Sports;

namespace Pulp.Tests.Sports;

/// <summary>
/// G10/G15 companion: free text from a feed (a game's headline, a series description) can read
/// "Finals - Game 6 (Harbor Gulls lead 3-2)". It may be <i>matched against</i> (is this the championship?)
/// but no string built from it may reach a held league, the shared calendar's fixtures or the print.
/// The feeds here are hostile on purpose; the expectation is the league's own name for its final.
/// </summary>
public class HeldLeagueCarriesNoFeedTextTests
{
    private const string Hostile = "Coastal Bowl LX - Game 6 (Harbor Gulls lead 3-2, Ridge Foxes eliminated)";
    private static readonly string[] Leaks = ["Game 6", "lead", "3-2", "eliminated", "Foxes", "Gulls"];

    private static readonly ViewerId Robin = new("robin-example");
    private static readonly DateTimeOffset Now = new(2026, 1, 20, 12, 0, 0, TimeSpan.Zero);

    private const string EspnTeams = """
    { "sports": [ { "leagues": [ { "teams": [
      { "team": { "id": "11", "displayName": "Harbor Gulls" } }, { "team": { "id": "12", "displayName": "Ridge Foxes" } } ] } ] } ] }
    """;

    private static string EspnBoard => $$"""
    { "leagues": [ { "season": { "startDate": "2025-08-01T07:00Z", "endDate": "2026-02-12T07:59Z", "type": { "type": 3 } } } ],
      "events": [ { "id": "e9", "date": "2026-01-25T23:30Z", "season": { "type": 3 },
        "competitions": [ { "competitors": [
          { "homeAway": "home", "team": { "id": "11", "displayName": "Harbor Gulls" } },
          { "homeAway": "away", "team": { "id": "12", "displayName": "Ridge Foxes" } } ],
          "notes": [ { "headline": "{{Hostile}}" } ] } ] } ] }
    """;

    [Fact]
    public async Task AHostileHeadlineNeverReachesAHeldLeagueOrAFixture()
    {
        var handler = new RoutedHandler().Route("/teams", EspnTeams).Route("/scoreboard", EspnBoard);
        var league = new EspnLeague(new LeagueId("cfl-invented"), "Coastal Football League", "gridball", "cfl", ["Coastal Bowl"]);
        var source = new EspnSource(new HttpClient(handler), [league]);

        var schedule = await source.GetScheduleAsync(league.Id, new SportsWindow(new DateOnly(2026, 1, 20), 0, 7), CancellationToken.None);
        var feed = LeagueFeeds.Assemble(schedule, [], []);
        var gulls = feed.Teams.Single(team => team.Name == "Harbor Gulls");
        var game = feed.Schedule.Single();
        var held = SpoilerHold.Present(feed, [gulls.Id], [game.Id], Now.AddDays(10), peeking: false);

        var sheet = Assert.IsType<HeldLeague>(held);
        Assert.Equal("Coastal Bowl", sheet.Championship!.Name);
        var fixtures = SportsFollowing.Fixtures([feed], [new SportsFollow(Robin, gulls.Id)], Now);
        var line = Assert.Single(fixtures, fixture => fixture.Kind == FixtureKind.ChampionshipLine);
        Assert.Equal("Coastal Bowl", line.Name);

        // Only the held league's championship is guarded here: team names are feed text by nature, the headline is not.
        Assert.DoesNotContain(Hostile, sheet.Championship.Name + line.Name);
        foreach (var leaked in Leaks)
        {
            Assert.DoesNotContain(leaked, sheet.Championship.Name + line.Name);
        }
    }
}
