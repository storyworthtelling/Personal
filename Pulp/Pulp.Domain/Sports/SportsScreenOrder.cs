namespace Pulp.Domain.Sports;

/// <summary>
/// The order within a league (D236): the teams with a recording waiting first, then by name; a team's
/// games as recordings still running oldest first, then the finished games (over by the clock, not yet
/// watched) newest first, then its next game. Stable, so two draws of the same
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
        var all = games.ToList();
        var running = all.Where(game => !game.Over && game.Recorded)
            .OrderBy(game => game.StartsAt).ThenBy(game => game.Id.Value, StringComparer.OrdinalIgnoreCase);
        var finished = all.Where(game => game.Over)
            .OrderByDescending(game => game.StartsAt).ThenBy(game => game.Id.Value, StringComparer.OrdinalIgnoreCase);
        var next = all.Where(game => !game.Over && !game.Recorded)
            .OrderBy(game => game.StartsAt).ThenBy(game => game.Id.Value, StringComparer.OrdinalIgnoreCase);
        return running.Concat(finished).Concat(next)
            .ToList();
    }
}
