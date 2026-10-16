namespace Pulp.Domain.Sports;

/// <summary>One game row (mockup 07): the teams, the day and time, the channel, the stage chip, and whether it is a recording to play.</summary>
public sealed record GameRow(
    GameId Id,
    string Title,
    string Detail,
    string? ChannelLine,
    string? Chip,
    bool Recorded,
    DateTimeOffset StartsAt,
    string? Channel = null,
    bool HighlightsOffered = false,
    string? HighlightsLabel = null)
{
    /// <summary>
    /// A row from the sheet's own <see cref="GameLine"/>. This is the only way to make one, so a row can
    /// only show what a held or open sheet chose to carry.
    /// </summary>
    public static GameRow From(GameLine game, DateTimeOffset now, TimeZoneInfo zone, bool highlights = false)
    {
        ArgumentNullException.ThrowIfNull(game);
        var title = game.Away.Name + " at " + game.Home.Name;
        var offered = highlights && HighlightsOffer.IsOver(game.StartsAt, now);
        return new GameRow(
            game.Id,
            title,
            SportsScreenText.GameDetail(game, now, zone),
            SportsScreenText.ChannelLine(game.Channel),
            SportsScreenText.StageChip(game.Stage),
            game.Recorded,
            game.StartsAt,
            string.IsNullOrWhiteSpace(game.Channel) ? null : game.Channel.Trim(),
            offered,
            offered ? HighlightsOffer.Label(title, game.StartsAt, zone) : null);
    }
}

/// <summary>A followed team inside its league: folded or open onto its games (D241, D236).</summary>
public sealed record TeamPlan(
    TeamId Id,
    string Name,
    string BadgeArt,
    bool Open,
    bool HasRecording,
    IReadOnlyList<GameRow> Games,
    string? EmptyLine)
{
    /// <summary>The quiet fold line over watched, finished games (D330); null when the team has none or the league is held.</summary>
    public string? EarlierLine { get; init; }

    /// <summary>Whether the viewer has opened the Earlier fold; it starts folded.</summary>
    public bool EarlierOpen { get; init; }

    public IReadOnlyList<GameRow> Earlier { get; init; } = [];
}

/// <summary>
/// A championship at league level, outside any team (D239, D240). <paramref name="TeamsLine"/> is absent
/// while the league is held: a held championship is its name and date and nothing more.
/// </summary>
public sealed record ChampionshipRow(string Name, string Detail, string Chip, string? TeamsLine);

/// <summary>Which page a league's link opens. Only an open league offers any of them.</summary>
public enum SportsLeaguePage
{
    Standings,
    Bracket,
    Schedule,
}

public sealed record LeagueLink(SportsLeaguePage Kind, string Label);

/// <summary>
/// One league as the screen draws it: its line (badge, name, the Spoilers tag or the state of a folded
/// league), then — when open — its teams, its championship, and the pages and highlights an open sheet
/// carries. A held league's plan has none of the last two, because its sheet has none.
/// </summary>
public sealed record LeaguePlan(
    LeagueId Id,
    string Name,
    string BadgeArt,
    bool Open,
    bool SpoilersTag,
    string? LineNote,
    IReadOnlyList<TeamPlan> Teams,
    ChampionshipRow? Championship,
    IReadOnlyList<Highlight> Highlights,
    IReadOnlyList<LeagueLink> Links);

/// <summary>
/// Builds what the Sports screen draws from the page the services hand over (D241, D242). The viewer's
/// folds since the page was read arrive as <paramref name="overlay"/>, which takes precedence over the starting rule.
/// </summary>
public static class SportsScreenPlan
{
    public const string NothingScheduled = "Nothing scheduled.";

    public const string EarlierLabel = "Earlier";

    public static IReadOnlyList<LeaguePlan> For(SportsPage page, FoldMemory overlay, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(zone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return page.Sheets.Select(sheet => Of(sheet, page.Fold, overlay, now, today, zone)).ToList();
    }

    private static LeaguePlan Of(LeagueSheet sheet, LeagueFold fold, FoldMemory overlay, DateTimeOffset now, DateOnly today, TimeZoneInfo zone)
    {
        var (line, teams) = sheet switch
        {
            HeldLeague held => (held.Line, held.Teams),
            OpenLeague opened => (opened.Line, opened.Teams),
            _ => throw new ArgumentOutOfRangeException(nameof(sheet), sheet, "A league sheet that is neither held nor open."),
        };

        var open = overlay.Remembered(FoldMemory.LeagueKey(line.Id)) ?? fold.IsLeagueOpen(line.Id);
        var highlights = sheet is OpenLeague offering && HighlightsOffer.Allowed(offering);
        var tagged = sheet is HeldLeague || sheet is OpenLeague { IsPeek: true };
        var earlier = sheet is OpenLeague { IsPeek: false } plain ? plain.Earlier : [];
        var teamPlans = SportsScreenOrder.Teams(teams).Select(team => TeamOf(line.Id, team, fold, overlay, now, zone, highlights, earlier)).ToList();

        var championship = sheet switch
        {
            HeldLeague held => held.Championship is { } champ
                ? new ChampionshipRow(champ.Name, SportsScreenText.ChampionshipDetail(champ.StartsAt, zone), SportsScreenText.StageChip(GameStage.Championship)!, null)
                : null,
            OpenLeague opened => opened.Championship is { } champ
                ? ChampionshipOf(champ, zone)
                : null,
            _ => null,
        };

        return new LeaguePlan(
            line.Id,
            line.Name,
            line.BadgeArt,
            open,
            tagged,
            tagged || open ? null : SportsScreenText.LeagueState(line, today),
            teamPlans,
            championship,
            sheet is OpenLeague visible ? visible.Highlights : [],
            sheet is OpenLeague links ? LinksOf(links) : []);
    }

    internal static ChampionshipRow ChampionshipOf(ChampionshipLine champ, TimeZoneInfo zone) =>
        new(
            champ.Name,
            SportsScreenText.ChampionshipDetail(champ.StartsAt, zone),
            SportsScreenText.StageChip(GameStage.Championship)!,
            champ.Teams.Count == 0 ? null : string.Join(champ.Teams.Count == 2 ? " vs " : " · ", champ.Teams.Select(team => team.Name)));

    private static TeamPlan TeamOf(LeagueId league, TeamGames team, LeagueFold fold, FoldMemory overlay, DateTimeOffset now, TimeZoneInfo zone, bool highlights, IReadOnlyList<TeamEarlier> earlier)
    {
        var hasRecording = team.Games.Any(game => game.Recorded);
        var open = overlay.Remembered(FoldMemory.TeamKey(league, team.Team.Id)) ?? fold.IsTeamOpen(league, team.Team.Id, hasRecording);
        var games = SportsScreenOrder.Games(team.Games).Select(game => GameRow.From(game, now, zone, highlights)).ToList();
        var earlierRows = earlier
            .Where(entry => entry.Team == team.Team.Id)
            .SelectMany(entry => entry.Games)
            .Select(game => GameRow.From(game, now, zone, highlights))
            .ToList();
        return new TeamPlan(
            team.Team.Id, team.Team.Name, team.Team.BadgeArt, open, hasRecording, games,
            games.Count == 0 && earlierRows.Count == 0 ? NothingScheduled : null)
        {
            Earlier = earlierRows,
            EarlierLine = earlierRows.Count == 0 ? null : EarlierLabel,
            EarlierOpen = earlierRows.Count > 0 && (overlay.Remembered(FoldMemory.EarlierKey(league, team.Team.Id)) ?? false),
        };
    }

    private static IReadOnlyList<LeagueLink> LinksOf(OpenLeague league)
    {
        var links = new List<LeagueLink>();
        if (league.Standings.Count > 0)
        {
            links.Add(new LeagueLink(SportsLeaguePage.Standings, "Standings"));
        }

        if (league.Championship is not null || SportsScreenPages.PlayoffGames(league).Count > 0)
        {
            links.Add(new LeagueLink(SportsLeaguePage.Bracket, "Playoff bracket"));
        }

        if (league.Teams.Any(team => team.Games.Count > 0))
        {
            links.Add(new LeagueLink(SportsLeaguePage.Schedule, "Schedule"));
        }

        return links;
    }
}
