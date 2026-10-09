namespace Pulp.Domain.Sports;

/// <summary>
/// What the Sports tab draws for one followed league: either <see cref="HeldLeague"/> or
/// <see cref="OpenLeague"/>, never a league with a flag saying which. The split is the spoiler rule
/// made structural (G10): a held league is a different type, and that type has nowhere to put a
/// score, a standing, a highlight or a bracket. <c>HeldLeagueCarriesNoScoreTests</c> walks it.
/// </summary>
public abstract record LeagueSheet
{
    private protected LeagueSheet()
    {
    }
}

/// <summary>The league's one line: badge, name and its state (D241).</summary>
public sealed record LeagueLine(LeagueId Id, string Name, string BadgeArt, SeasonPhase Phase, DateOnly? NextDate);

/// <summary>
/// One game row. <paramref name="Recorded"/> marks an unwatched recording, which plays from the
/// start on the named channel (D55, D57); otherwise it is the team's next game (D236).
/// </summary>
public sealed record GameLine(
    GameId Id,
    Team Home,
    Team Away,
    DateTimeOffset StartsAt,
    GameStage Stage,
    string Channel,
    bool Recorded);

/// <summary>A followed team inside its league, and the games it shows (D236).</summary>
public sealed record TeamGames(Team Team, IReadOnlyList<GameLine> Games);

/// <summary>A held league's championship: its name and its date, and no teams (D239, D242).</summary>
public sealed record HeldChampionship(string Name, DateTimeOffset StartsAt);

/// <summary>
/// A league under the spoiler hold (D70, D237, D242). Its line carries the Spoilers tag; standings,
/// highlights, the bracket and any fixture whose existence reveals a result are simply absent —
/// not hidden behind a flag, absent from the type.
/// </summary>
public sealed record HeldLeague(LeagueLine Line, IReadOnlyList<TeamGames> Teams, HeldChampionship? Championship)
    : LeagueSheet;

/// <summary>A championship with whoever is in it, once nothing is held.</summary>
public sealed record ChampionshipLine(string Name, DateTimeOffset StartsAt, IReadOnlyList<Team> Teams);

/// <summary>
/// A league with nothing held, or a held league during its one peek (<paramref name="IsPeek"/>,
/// D183): standings in rank order and the league's highlights.
/// </summary>
public sealed record OpenLeague(
    LeagueLine Line,
    IReadOnlyList<TeamGames> Teams,
    ChampionshipLine? Championship,
    IReadOnlyList<Team> Standings,
    IReadOnlyList<Highlight> Highlights,
    bool IsPeek) : LeagueSheet;
