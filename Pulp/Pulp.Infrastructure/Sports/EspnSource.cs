using System.Globalization;
using System.Text.Json;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Infrastructure.Sports;

/// <summary>One league ESPN's public JSON serves: its path (<c>/sports/{Sport}/{Slug}</c>) and the words that name its championship game.</summary>
public sealed record EspnLeague(LeagueId Id, string Name, string Sport, string Slug, IReadOnlyList<string> ChampionshipMarkers);

/// <summary>The leagues the shipped table (<see cref="SportsSourceTable.Default"/>) reads from ESPN.</summary>
public static class EspnLeagues
{
    public static IReadOnlyList<EspnLeague> Default { get; } =
    [
        new(new LeagueId("nfl"), "NFL", "football", "nfl", ["Super Bowl"]),
        new(new LeagueId("nhl"), "NHL", "hockey", "nhl", ["Stanley Cup Final"]),
        new(new LeagueId("nba"), "NBA", "basketball", "nba", ["NBA Finals"]),
        new(new LeagueId("wnba"), "WNBA", "basketball", "wnba", ["WNBA Finals"]),
        new(new LeagueId("nwsl"), "NWSL", "soccer", "usa.nwsl", ["NWSL Championship"]),
        new(new LeagueId("afl"), "AFL", "australian-football", "afl", ["Grand Final"]),
    ];
}

/// <summary>
/// Every league without a league feed of its own, from ESPN's public JSON (D208). ESPN publishes no terms
/// for it and may change it without notice (finding §1.2), so a response that is not the shape below is
/// <see cref="LookupFailure.Malformed"/> and the cached copy stands; nothing is guessed. Shapes, measured
/// live on <c>site.api.espn.com</c>:
/// <list type="bullet">
/// <item><c>/apis/site/v2/sports/{sport}/{slug}/scoreboard?dates=yyyyMMdd</c> — <c>leagues[0].season</c>
/// (<c>startDate</c>, <c>endDate</c>, <c>type.type</c>: 1 pre, 2 regular, 3 post) and <c>events[]</c> with <c>id</c>,
/// <c>date</c>, <c>season.type</c>, <c>competitions[0].competitors[]</c> (<c>homeAway</c>, <c>team</c>),
/// <c>competitions[0].notes[].headline</c> and <c>competitions[0].broadcasts[]</c> (<c>market</c>, <c>names[]</c>).
/// A date <i>range</i> was refused with HTTP 400 when measured, so each day is asked for singly.</item>
/// <item><c>.../teams</c> — <c>sports[0].leagues[0].teams[].team</c>.</item>
/// <item><c>/apis/v2/sports/{sport}/{slug}/standings</c> — groups at <c>children[]</c> or the root, each with
/// <c>standings.entries[]</c>; a team's rank is its <c>rank</c> or <c>playoffSeed</c> stat.</item>
/// </list>
/// The same responses carry scores, winners, records, leaders and the other stats. <b>None is read</b>
/// (G15): only the named fields above are taken, and the one array of stats is searched for a rank by
/// name and for nothing else.
/// </summary>
public sealed class EspnSource : ISportsSource
{
    private const string SiteHost = "https://site.api.espn.com/apis/site/v2/sports";
    private const string StandingsHost = "https://site.api.espn.com/apis/v2/sports";
    private static readonly Dictionary<string, string> NoHeaders = [];

    private readonly ProviderHttp http;
    private readonly Dictionary<LeagueId, EspnLeague> leagues;

    public EspnSource(HttpClient httpClient, IReadOnlyList<EspnLeague> leagues, ILookupCounter? counter = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(leagues);
        this.leagues = leagues.ToDictionary(league => league.Id);
        Leagues = leagues.Select(league => league.Id).ToList();
        http = new ProviderHttp(httpClient, LookupSource.Espn, null, counter, null);
    }

    public IReadOnlyList<LeagueId> Leagues { get; }

    public async Task<LeagueSchedule> GetScheduleAsync(LeagueId requested, SportsWindow window, CancellationToken cancellationToken)
    {
        var league = Served(requested);
        var teams = await ReadTeamsAsync(league, cancellationToken);
        var known = teams.ToDictionary(team => team.Id);

        var games = new List<(Game Game, IReadOnlyList<string> Notes)>();
        ScoreboardSeason? season = null;
        var badge = string.Empty;
        foreach (var day in window.Days())
        {
            var what = "a scoreboard request";
            var response = await http.GetAsync(
                new Uri($"{SiteHost}/{league.Sport}/{league.Slug}/scoreboard?dates={day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}&limit=300"),
                NoHeaders, what, cancellationToken);
            using var document = http.Parse(response, what);
            var root = document.RootElement;
            var block = root.Child("leagues").Items().FirstOrDefault();
            if (block.ValueKind != JsonValueKind.Object)
            {
                throw http.Malformed(what, "no leagues block");
            }

            if (season is null || day == window.Today)
            {
                season = ReadSeason(block.Child("season"), what);
                badge = block.Child("logos").Items().Select(logo => logo.Text("href")).FirstOrDefault(href => !string.IsNullOrWhiteSpace(href)) ?? badge;
            }

            games.AddRange(ReadEvents(root.Child("events"), known, league));
        }

        var ordered = games.DistinctBy(entry => entry.Game.Id).OrderBy(entry => entry.Game.StartsAt).ToList();
        var distinct = ordered.Select(entry => entry.Game).ToList();
        var championship = ordered.Where(entry => entry.Game.Stage == GameStage.Championship).Select(entry => ChampionshipOf(entry.Game, entry.Notes, league)).FirstOrDefault();
        return new LeagueSchedule(LeagueOf(league, season!, badge, window.Today, distinct), teams, distinct, championship);
    }

    public async Task<IReadOnlyList<Team>> GetStandingsOrderAsync(LeagueId requested, CancellationToken cancellationToken)
    {
        var league = Served(requested);
        var what = "a standings request";
        var response = await http.GetAsync(new Uri($"{StandingsHost}/{league.Sport}/{league.Slug}/standings"), NoHeaders, what, cancellationToken);
        using var document = http.Parse(response, what);

        var ranked = new List<(double Rank, int Group, int Index, Team Team)>();
        var groups = 0;
        foreach (var group in Groups(document.RootElement))
        {
            var index = 0;
            foreach (var entry in group.Child("standings").Child("entries").Items())
            {
                var team = TeamOf(entry.Child("team"));
                if (team is not null)
                {
                    ranked.Add((RankOf(entry.Child("stats")), groups, index++, team));
                }
            }

            groups++;
        }

        if (ranked.Count == 0)
        {
            throw http.Malformed(what, "no standings entries");
        }

        // Seeds are per group (conference, division), so the groups' firsts come before their seconds.
        return ranked.OrderBy(entry => entry.Rank).ThenBy(entry => entry.Group).ThenBy(entry => entry.Index)
            .Select(entry => entry.Team).DistinctBy(team => team.Id).ToList();
    }

    private EspnLeague Served(LeagueId requested) =>
        leagues.TryGetValue(requested, out var league)
            ? league
            : throw new ArgumentException($"ESPN's source was not given the league '{requested}'.", nameof(requested));

    private static IEnumerable<JsonElement> Groups(JsonElement node)
    {
        if (node.Child("standings").ValueKind == JsonValueKind.Object)
        {
            yield return node;
        }

        foreach (var child in node.Child("children").Items())
        {
            foreach (var group in Groups(child))
            {
                yield return group;
            }
        }
    }

    // The one place a stats array is read: for a rank, by name, and nothing else in it.
    private static double RankOf(JsonElement stats)
    {
        foreach (var name in new[] { "rank", "playoffSeed" })
        {
            foreach (var stat in stats.Items().Where(stat => stat.Text("name") == name))
            {
                if (stat.Child("value").ValueKind == JsonValueKind.Number && stat.Child("value").TryGetDouble(out var rank))
                {
                    return rank;
                }
            }
        }

        return double.MaxValue;
    }

    private async Task<IReadOnlyList<Team>> ReadTeamsAsync(EspnLeague league, CancellationToken cancellationToken)
    {
        var what = "a teams request";
        var response = await http.GetAsync(new Uri($"{SiteHost}/{league.Sport}/{league.Slug}/teams?limit=100"), NoHeaders, what, cancellationToken);
        using var document = http.Parse(response, what);
        var teams = document.RootElement.Child("sports").Items().FirstOrDefault().Child("leagues").Items().FirstOrDefault().Child("teams");
        if (teams.ValueKind != JsonValueKind.Array)
        {
            throw http.Malformed(what, "no teams array");
        }

        return teams.Items().Select(entry => TeamOf(entry.Child("team"))).OfType<Team>().DistinctBy(team => team.Id).ToList();
    }

    private static Team? TeamOf(JsonElement team)
    {
        var id = team.Text("id");
        var name = team.Text("displayName");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var art = team.Text("logo") ?? team.Child("logos").Items().Select(logo => logo.Text("href")).FirstOrDefault(href => !string.IsNullOrWhiteSpace(href)) ?? string.Empty;
        return new Team(new TeamId(id), name, art);
    }

    private sealed record ScoreboardSeason(DateOnly Start, DateOnly End, int Type);

    private ScoreboardSeason ReadSeason(JsonElement season, string what)
    {
        DateOnly Day(string name) =>
            season.Text(name) is { Length: >= 10 } text
            && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : throw http.Malformed(what, $"the season has no usable '{name}'");

        return new ScoreboardSeason(Day("startDate"), Day("endDate"), season.Child("type").Number("type") ?? 0);
    }

    private static List<(Game Game, IReadOnlyList<string> Notes)> ReadEvents(JsonElement events, Dictionary<TeamId, Team> known, EspnLeague league)
    {
        var games = new List<(Game, IReadOnlyList<string>)>();
        foreach (var raw in events.Items())
        {
            var competition = raw.Child("competitions").Items().FirstOrDefault();
            var competitors = competition.Child("competitors").Items().ToList();
            var home = competitors.FirstOrDefault(competitor => competitor.Text("homeAway") == "home");
            var away = competitors.FirstOrDefault(competitor => competitor.Text("homeAway") == "away");
            var id = raw.Text("id");
            var homeTeam = TeamOf(home.Child("team"));
            var awayTeam = TeamOf(away.Child("team"));
            if (string.IsNullOrWhiteSpace(id) || homeTeam is null || awayTeam is null
                || !DateTimeOffset.TryParse(raw.Text("date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var startsAt))
            {
                // An all-star or a placeholder with no two clubs is not a game anyone follows a team into.
                continue;
            }

            var notes = Headlines(raw, competition);
            var stage = StageOf(raw.Child("season").Number("type"), notes, league);
            games.Add((new Game(
                new GameId(id), known.GetValueOrDefault(homeTeam.Id, homeTeam), known.GetValueOrDefault(awayTeam.Id, awayTeam),
                startsAt, stage, ChannelOf(competition.Child("broadcasts"))), notes));
        }

        return games;
    }

    private static List<string> Headlines(JsonElement raw, JsonElement competition) =>
        competition.Child("notes").Items().Concat(raw.Child("notes").Items())
            .Select(note => note.Text("headline")).OfType<string>().Where(text => text.Length > 0).ToList();

    // Anything this build does not know is held as a revealing playoff: wrongly held is a fold, wrongly shown is a spoiler.
    private static GameStage StageOf(int? seasonType, IReadOnlyList<string> notes, EspnLeague league) => seasonType switch
    {
        1 => GameStage.Preseason,
        2 => GameStage.RegularSeason,
        3 when notes.Any(note => league.ChampionshipMarkers.Any(marker => note.Contains(marker, StringComparison.OrdinalIgnoreCase))) => GameStage.Championship,
        _ => GameStage.Playoffs,
    };

    private static Championship ChampionshipOf(Game game, IReadOnlyList<string> notes, EspnLeague league)
    {
        var name = notes
            .FirstOrDefault(note => league.ChampionshipMarkers.Any(marker => note.Contains(marker, StringComparison.OrdinalIgnoreCase))) ?? league.Name + " championship";
        return new Championship(game.Id, name, game.StartsAt, [game.Home, game.Away]);
    }

    /// <summary>The national broadcast first, else the first one named, else none.</summary>
    private static string ChannelOf(JsonElement broadcasts)
    {
        var named = broadcasts.Items().Where(broadcast => broadcast.Child("names").Items().Any()).ToList();
        var chosen = named.FirstOrDefault(broadcast => broadcast.Text("market") == "national");
        if (chosen.ValueKind != JsonValueKind.Object)
        {
            chosen = named.FirstOrDefault();
        }

        return chosen.ValueKind == JsonValueKind.Object
            ? string.Join(" · ", chosen.Child("names").Items().Where(name => name.ValueKind == JsonValueKind.String).Select(name => name.GetString()!))
            : string.Empty;
    }

    private static League LeagueOf(EspnLeague league, ScoreboardSeason season, string badge, DateOnly today, IReadOnlyList<Game> games)
    {
        var upcoming = games.Where(game => DateOnly.FromDateTime(game.StartsAt.UtcDateTime) >= today).ToList();
        DateOnly? NextGameDate(GameStage? stage = null) =>
            upcoming.Where(game => stage is null || game.Stage == stage).Select(game => (DateOnly?)DateOnly.FromDateTime(game.StartsAt.UtcDateTime)).FirstOrDefault();

        if (today < season.Start)
        {
            var preseason = NextGameDate(GameStage.Preseason);
            return preseason is { } first
                ? new League(league.Id, league.Name, badge, SeasonPhase.Preseason, season.End, first)
                : new League(league.Id, league.Name, badge, SeasonPhase.OffSeason, season.End, season.Start);
        }

        if (today > season.End)
        {
            return new League(league.Id, league.Name, badge, SeasonPhase.OffSeason, season.End, null);
        }

        var nearest = games.OrderBy(game => Math.Abs((DateOnly.FromDateTime(game.StartsAt.UtcDateTime).DayNumber - today.DayNumber))).ThenBy(game => game.StartsAt).FirstOrDefault();
        var phase = nearest is not null
            ? nearest.Stage switch
            {
                GameStage.Preseason => SeasonPhase.Preseason,
                GameStage.RegularSeason => SeasonPhase.RegularSeason,
                _ => SeasonPhase.Postseason,
            }
            : season.Type switch
            {
                1 => SeasonPhase.Preseason,
                3 => SeasonPhase.Postseason,
                4 => SeasonPhase.OffSeason,
                _ => SeasonPhase.RegularSeason,
            };
        return new League(league.Id, league.Name, badge, phase, season.End, phase == SeasonPhase.Preseason ? NextGameDate() : null);
    }
}
