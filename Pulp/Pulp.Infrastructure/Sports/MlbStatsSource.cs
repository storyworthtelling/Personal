using System.Globalization;
using System.Text.Json;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Infrastructure.Sports;

/// <summary>
/// Baseball from MLB's own Stats API (D208). Its notice permits "individual, non-commercial, non-bulk
/// use"; Pulp is a household's own app and asks for a handful of small documents per refresh. The
/// shapes are the API's own, read from the live responses:
/// <list type="bullet">
/// <item><c>/api/v1/seasons?sportId=1&amp;season=Y</c> — <c>seasons[0]</c> carries the season's dates
/// (<c>springStartDate</c>, <c>regularSeasonStartDate</c>, <c>regularSeasonEndDate</c>, <c>seasonEndDate</c>).</item>
/// <item><c>/api/v1/teams?sportId=1&amp;season=Y</c> — <c>teams[].id</c>, <c>teams[].name</c>.</item>
/// <item><c>/api/v1/schedule?sportId=1&amp;startDate&amp;endDate&amp;hydrate=broadcasts</c> — <c>dates[].games[]</c>
/// with <c>gamePk</c>, <c>gameDate</c>, <c>gameType</c> (S spring, E exhibition, R regular, A all-star, F wild card,
/// D division, L league championship, W World Series), <c>teams.{home,away}.team.id</c> and
/// <c>broadcasts[]</c> (<c>name</c>, <c>type</c>, <c>isNational</c>).</item>
/// <item><c>/api/v1/standings?leagueId=103,104</c> (the current season) — <c>records[].teamRecords[]</c>, each with
/// <c>team</c> and <c>sportRank</c> (the team's rank across the whole sport, as text).</item>
/// </list>
/// Those responses also carry scores, records, winners and run totals. <b>None is read</b>: the adapter
/// takes the named fields above out of the document and nothing else, and it has no model of the wire to
/// parse a score into (G15, <c>SportsSourcesCarryNoScoreTests</c>). The game's status is not read either:
/// "has it started" is the start time against the clock, and "was it watched" is the watch evidence.
/// </summary>
public sealed class MlbStatsSource : ISportsSource
{
    public static readonly LeagueId DefaultLeague = new("mlb");

    private const string Host = "https://statsapi.mlb.com/api/v1";

    // The two leagues' ids in the Stats API (103 and 104), which the standings call is scoped to.
    private const string StandingsLeagues = "103,104";

    // mlbstatic.com serves the club and league marks the Stats API's own pages use; artwork in a
    // content slot (D238), loaded from the host that publishes it.
    private const string TeamBadge = "https://www.mlbstatic.com/team-logos/{0}.svg";
    private const string LeagueBadge = "https://www.mlbstatic.com/team-logos/league-on-dark/1.svg";

    private readonly ProviderHttp http;
    private readonly LeagueId league;
    private readonly string leagueName;

    public MlbStatsSource(HttpClient httpClient, ILookupCounter? counter = null, LeagueId? league = null, string leagueName = "MLB")
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(leagueName);
        this.league = league ?? DefaultLeague;
        this.leagueName = leagueName;
        http = new ProviderHttp(httpClient, LookupSource.MlbStats, null, counter, null);
    }

    public IReadOnlyList<LeagueId> Leagues => [league];

    public async Task<LeagueSchedule> GetScheduleAsync(LeagueId requested, SportsWindow window, CancellationToken cancellationToken)
    {
        Require(requested);
        var year = window.Today.Year;
        var season = await ReadSeasonAsync(year, cancellationToken);
        var teams = await ReadTeamsAsync(year, cancellationToken);
        var byId = teams.ToDictionary(team => team.Id);

        var what = "a schedule request";
        var response = await http.GetAsync(
            Endpoint("/schedule", ("sportId", "1"), ("startDate", Iso(window.From)), ("endDate", Iso(window.To)), ("hydrate", "broadcasts")),
            NoHeaders, what, cancellationToken);
        using var document = http.Parse(response, what);
        var games = ReadGames(document.RootElement, byId, what);

        var championship = await ReadChampionshipAsync(year, byId, cancellationToken);
        var leagueRow = await LeagueOfAsync(season, window.Today, games, cancellationToken);
        return new LeagueSchedule(leagueRow, teams, games, championship);
    }

    public async Task<IReadOnlyList<Team>> GetStandingsOrderAsync(LeagueId requested, CancellationToken cancellationToken)
    {
        Require(requested);
        var what = "a standings request";
        var response = await http.GetAsync(
            Endpoint("/standings", ("leagueId", StandingsLeagues)),
            NoHeaders, what, cancellationToken);
        using var document = http.Parse(response, what);
        var groups = document.RootElement.Child("records");
        if (groups.ValueKind != JsonValueKind.Array)
        {
            throw http.Malformed(what, "no records array");
        }

        var ranked = new List<(int Rank, Team Team)>();
        foreach (var entry in groups.Items().SelectMany(group => group.Child("teamRecords").Items()))
        {
            var team = TeamOf(entry.Child("team"));
            if (team is null)
            {
                continue;
            }

            var rank = int.TryParse(entry.Text("sportRank"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MaxValue;
            ranked.Add((rank, team));
        }

        return ranked.OrderBy(row => row.Rank).ThenBy(row => row.Team.Name, StringComparer.OrdinalIgnoreCase)
            .Select(row => row.Team).DistinctBy(team => team.Id).ToList();
    }

    private static readonly Dictionary<string, string> NoHeaders = [];

    private void Require(LeagueId requested)
    {
        if (requested != league)
        {
            throw new ArgumentException($"The MLB Stats source serves '{league}', not '{requested}'.", nameof(requested));
        }
    }

    private static Uri Endpoint(string path, params (string Name, string Value)[] query) =>
        new(Host + path + "?" + string.Join('&', query.Select(pair => pair.Name + "=" + pair.Value)));

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record SeasonDates(
        DateOnly SpringStart, DateOnly RegularStart, DateOnly RegularEnd, DateOnly SeasonEnd);

    private async Task<SeasonDates> ReadSeasonAsync(int year, CancellationToken cancellationToken)
    {
        var season = await TryReadSeasonAsync(year, cancellationToken);
        return season ?? throw http.Malformed("a season lookup", $"no season for {year}");
    }

    private async Task<SeasonDates?> TryReadSeasonAsync(int year, CancellationToken cancellationToken)
    {
        var what = "a season lookup";
        var response = await http.GetAsync(
            Endpoint("/seasons", ("sportId", "1"), ("season", year.ToString(CultureInfo.InvariantCulture))),
            NoHeaders, what, cancellationToken);
        using var document = http.Parse(response, what);
        var seasons = document.RootElement.Child("seasons");
        if (seasons.ValueKind != JsonValueKind.Array)
        {
            throw http.Malformed(what, "no seasons array");
        }

        var first = seasons.Items().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        DateOnly Day(string name) =>
            DateOnly.TryParseExact(first.Text(name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : throw http.Malformed(what, $"season {year} has no usable '{name}'");

        return new SeasonDates(Day("springStartDate"), Day("regularSeasonStartDate"), Day("regularSeasonEndDate"), Day("seasonEndDate"));
    }

    private async Task<IReadOnlyList<Team>> ReadTeamsAsync(int year, CancellationToken cancellationToken)
    {
        var what = "a teams request";
        var response = await http.GetAsync(
            Endpoint("/teams", ("sportId", "1"), ("season", year.ToString(CultureInfo.InvariantCulture))),
            NoHeaders, what, cancellationToken);
        using var document = http.Parse(response, what);
        var teams = document.RootElement.Child("teams");
        if (teams.ValueKind != JsonValueKind.Array)
        {
            throw http.Malformed(what, "no teams array");
        }

        return teams.Items().Select(TeamOf).OfType<Team>().DistinctBy(team => team.Id).ToList();
    }

    private static Team? TeamOf(JsonElement team)
    {
        var id = team.Number("id");
        var name = team.Text("name");
        return id is null || string.IsNullOrWhiteSpace(name)
            ? null
            : new Team(new TeamId(id.Value.ToString(CultureInfo.InvariantCulture)), name, string.Format(CultureInfo.InvariantCulture, TeamBadge, id.Value));
    }

    private static Team? Resolve(JsonElement side, Dictionary<TeamId, Team> known)
    {
        var team = TeamOf(side.Child("team"));
        return team is null ? null : known.GetValueOrDefault(team.Id, team);
    }

    private IReadOnlyList<Game> ReadGames(JsonElement root, Dictionary<TeamId, Team> known, string what)
    {
        var dates = root.Child("dates");
        if (root.ValueKind != JsonValueKind.Object || (dates.ValueKind != JsonValueKind.Array && dates.ValueKind != JsonValueKind.Undefined))
        {
            throw http.Malformed(what, "no dates array");
        }

        var games = new List<Game>();
        foreach (var raw in dates.Items().SelectMany(day => day.Child("games").Items()))
        {
            var id = raw.Long("gamePk");
            var home = Resolve(raw.Child("teams").Child("home"), known);
            var away = Resolve(raw.Child("teams").Child("away"), known);
            if (id is null || home is null || away is null || !Instant(raw.Text("gameDate"), out var startsAt))
            {
                // A placeholder fixture with no club on one side is not a game anyone can follow yet.
                continue;
            }

            games.Add(new Game(new GameId(id.Value.ToString(CultureInfo.InvariantCulture)), home, away, startsAt, StageOf(raw.Text("gameType")), ChannelOf(raw.Child("broadcasts"))));
        }

        return games.DistinctBy(game => game.Id).OrderBy(game => game.StartsAt).ToList();
    }

    // Anything this build does not know is held as a revealing playoff: wrongly held is a fold, wrongly shown is a spoiler.
    private static GameStage StageOf(string? gameType) => gameType switch
    {
        "S" or "E" => GameStage.Preseason,
        "R" or "A" => GameStage.RegularSeason,
        "W" => GameStage.Championship,
        _ => GameStage.Playoffs,
    };

    /// <summary>A national TV broadcast first, else the first TV one, else none (radio is not a channel to watch on).</summary>
    private static string ChannelOf(JsonElement broadcasts)
    {
        var television = broadcasts.Items().Where(broadcast => broadcast.Text("type") == "TV" && !string.IsNullOrWhiteSpace(broadcast.Text("name"))).ToList();
        var chosen = television.FirstOrDefault(broadcast => broadcast.Flag("isNational"));
        if (chosen.ValueKind != JsonValueKind.Object)
        {
            chosen = television.FirstOrDefault();
        }

        return chosen.ValueKind == JsonValueKind.Object ? chosen.Text("name")! : string.Empty;
    }

    private static bool Instant(string? text, out DateTimeOffset at) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out at);

    private async Task<Championship?> ReadChampionshipAsync(int year, Dictionary<TeamId, Team> known, CancellationToken cancellationToken)
    {
        var what = "a championship request";
        var response = await http.GetAsync(
            Endpoint("/schedule", ("sportId", "1"), ("gameType", "W"), ("season", year.ToString(CultureInfo.InvariantCulture))),
            NoHeaders, what, cancellationToken);
        using var document = http.Parse(response, what);
        var finals = document.RootElement.Child("dates").Items().SelectMany(day => day.Child("games").Items())
            .Select(raw => (Raw: raw, Ok: Instant(raw.Text("gameDate"), out var at), At: at))
            .Where(entry => entry.Ok)
            .OrderBy(entry => entry.At)
            .ToList();
        if (finals.Count == 0)
        {
            return null;
        }

        var first = finals[0].Raw;
        var id = first.Long("gamePk");
        if (id is null)
        {
            return null;
        }

        var teams = new[] { Resolve(first.Child("teams").Child("home"), known), Resolve(first.Child("teams").Child("away"), known) }
            .OfType<Team>().ToList();
        var name = first.Text("seriesDescription") is { Length: > 0 } series ? series : "Championship";
        return new Championship(new GameId(id.Value.ToString(CultureInfo.InvariantCulture)), name, finals[0].At, teams);
    }

    /// <summary>Where the league is, from the season's own dates and today (D184, D235).</summary>
    private async Task<League> LeagueOfAsync(SeasonDates season, DateOnly today, IReadOnlyList<Game> games, CancellationToken cancellationToken)
    {
        if (today > season.SeasonEnd)
        {
            var next = await TryReadSeasonAsync(today.Year + 1, cancellationToken);
            return new League(league, leagueName, LeagueBadge, SeasonPhase.OffSeason, next?.SeasonEnd ?? season.SeasonEnd, next?.RegularStart);
        }

        if (today < season.SpringStart)
        {
            return new League(league, leagueName, LeagueBadge, SeasonPhase.OffSeason, season.SeasonEnd, season.RegularStart);
        }

        if (today < season.RegularStart)
        {
            var nextGame = games.Where(game => DateOnly.FromDateTime(game.StartsAt.UtcDateTime) >= today).Select(game => (DateOnly?)DateOnly.FromDateTime(game.StartsAt.UtcDateTime)).FirstOrDefault();
            return new League(league, leagueName, LeagueBadge, SeasonPhase.Preseason, season.SeasonEnd, nextGame ?? season.RegularStart);
        }

        var phase = today <= season.RegularEnd ? SeasonPhase.RegularSeason : SeasonPhase.Postseason;
        return new League(league, leagueName, LeagueBadge, phase, season.SeasonEnd, null);
    }
}
