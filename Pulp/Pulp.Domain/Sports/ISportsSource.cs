namespace Pulp.Domain.Sports;

/// <summary>
/// A league's teams, its games in a window, and its championship — the schedule half of a
/// <see cref="LeagueFeed"/>. Like <see cref="Game"/>, it has nowhere to put a result.
/// </summary>
public sealed record LeagueSchedule(
    League League,
    IReadOnlyList<Team> Teams,
    IReadOnlyList<Game> Games,
    Championship? Championship);

/// <summary>
/// What the Domain needs from a sports feed (D208): a league's schedule for a window, and its
/// standings as <b>rank order only</b>. Every return type is a Domain type that has no member able
/// to carry a score, a record, points or a winner — the adapters drop those fields at the wire and
/// never parse them into a model (G15, <c>SportsSourcesCarryNoScoreTests</c>). Highlights are not
/// here: they come from the league's official YouTube channel (D71), not from a schedule feed whose
/// terms say nothing about them.
/// </summary>
public interface ISportsSource
{
    /// <summary>The leagues this source is asked about.</summary>
    IReadOnlyList<LeagueId> Leagues { get; }

    /// <summary>The league's teams and its games in <paramref name="window"/>. Throws when the league is not one of <see cref="Leagues"/>.</summary>
    Task<LeagueSchedule> GetScheduleAsync(LeagueId league, SportsWindow window, CancellationToken cancellationToken);

    /// <summary>The league's teams best first. The order is all there is: no points, no record, no gap.</summary>
    Task<IReadOnlyList<Team>> GetStandingsOrderAsync(LeagueId league, CancellationToken cancellationToken);
}
