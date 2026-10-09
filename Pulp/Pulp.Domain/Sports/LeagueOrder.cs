namespace Pulp.Domain.Sports;

/// <summary>
/// D235: leagues closest to the end of their season come first; preseason leagues follow, then
/// off-season ones, each of those by its next date (soonest first). Ties fall to the name, so the
/// order never changes between two draws of the same data.
/// </summary>
public static class LeagueOrder
{
    public static IReadOnlyList<League> Order(IEnumerable<League> leagues)
    {
        ArgumentNullException.ThrowIfNull(leagues);
        return leagues
            .OrderBy(Band)
            .ThenBy(SortDate)
            .ThenBy(league => league.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(league => league.Id.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The league closest to its season's end, or none when nothing is in season.</summary>
    public static League? ClosestToEnd(IEnumerable<League> leagues) =>
        Order(leagues).FirstOrDefault(IsInSeason);

    public static bool IsInSeason(League league)
    {
        ArgumentNullException.ThrowIfNull(league);
        return league.Phase is SeasonPhase.RegularSeason or SeasonPhase.Postseason;
    }

    private static int Band(League league) => league.Phase switch
    {
        SeasonPhase.RegularSeason or SeasonPhase.Postseason => 0,
        SeasonPhase.Preseason => 1,
        SeasonPhase.OffSeason => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(league), league.Phase, "A season phase LeagueOrder has no band for."),
    };

    /// <summary>In season: the season's end. Otherwise: the next date, with an unknown one last.</summary>
    private static DateOnly SortDate(League league) =>
        IsInSeason(league) ? league.SeasonEnds : league.NextDate ?? DateOnly.MaxValue;
}
