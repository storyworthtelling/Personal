using Pulp.Domain.Core;

namespace Pulp.Domain.Sports;

/// <summary>Where a league is in its year (D184, D235).</summary>
public enum SeasonPhase
{
    Preseason,
    RegularSeason,
    Postseason,
    OffSeason,
}

/// <summary>The status chip on a game (D184).</summary>
public enum GameStage
{
    Preseason,
    RegularSeason,
    Playoffs,
    Championship,
}

/// <summary>
/// A league as the feed describes it. <paramref name="SeasonEnds"/> is the last day of the current
/// or coming season; <paramref name="NextDate"/> is the next game for a preseason league and the
/// return date for an off-season one (D235: "an off-season league is one line with its return date").
/// </summary>
public sealed record League(
    LeagueId Id,
    string Name,
    string BadgeArt,
    SeasonPhase Phase,
    DateOnly SeasonEnds,
    DateOnly? NextDate);

/// <summary>A team. Its badge is artwork from the feed, content in an artwork slot (D238).</summary>
public sealed record Team(TeamId Id, string Name, string BadgeArt);

/// <summary>
/// One game on a league's schedule. Deliberately no score: the adapters never return one (D55, G15).
/// </summary>
public sealed record Game(GameId Id, Team Home, Team Away, DateTimeOffset StartsAt, GameStage Stage, string Channel)
{
    public bool Involves(TeamId team) => Home.Id == team || Away.Id == team;

    /// <summary>"Away at Home", the way the Sports and Calendar rows name a game.</summary>
    public string Name => Away.Name + " at " + Home.Name;
}

/// <summary>A league's championship game or series. Its teams may not be known yet.</summary>
public sealed record Championship(GameId Id, string Name, DateTimeOffset StartsAt, IReadOnlyList<Team> Teams);

/// <summary>A league-wide highlight from the league's official channel (D71).</summary>
public sealed record Highlight(string VideoId, string Title);

/// <summary>
/// Everything the sports feed says about one league. Standings are rank order only — a table of
/// teams, never a table of points — but they still reveal results, which is why a held league
/// never sees them.
/// </summary>
public sealed record LeagueFeed(
    League League,
    IReadOnlyList<Team> Teams,
    IReadOnlyList<Game> Schedule,
    IReadOnlyList<Team> Standings,
    IReadOnlyList<Highlight> Highlights,
    Championship? Championship);

/// <summary>One adult following one team (D60: follows are per adult).</summary>
public sealed record SportsFollow(ViewerId Viewer, TeamId Team);
