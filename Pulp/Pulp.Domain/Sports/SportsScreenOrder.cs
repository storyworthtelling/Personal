namespace Pulp.Domain.Sports;

/// <summary>
/// The order within a league (D236): the teams with a recording waiting first, then by name; a team's
/// games as unwatched recordings oldest first, then its next game. Stable, so two draws of the same
/// page put the same row in the same place.
/// </summary>
public static class SportsScreenOrder
{
    public static IReadOnlyList<TeamGames> Teams(IEnumerable<TeamGames> teams)
    {
        ArgumentNullException.ThrowIfNull(teams);
        return teams
            .OrderBy(team => team.Games.Any(game => game.Recorded) ? 0 : 1)
            .ThenBy(team => team.Team.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(team => team.Team.Id.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<GameLine> Games(IEnumerable<GameLine> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        return games
            .OrderBy(game => game.Recorded ? 0 : 1)
            .ThenBy(game => game.StartsAt)
            .ThenBy(game => game.Id.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
