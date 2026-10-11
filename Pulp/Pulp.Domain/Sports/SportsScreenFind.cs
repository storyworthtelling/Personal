namespace Pulp.Domain.Sports;

/// <summary>One team the "Find a team to follow" field offers, with the league it brings in (D67).</summary>
public sealed record FindRow(Team Team, LeagueId League, string LeagueName, bool Following);

/// <summary>
/// The field's narrowing (mockup 07): a team is found by part of its name or its league's, without
/// case. Nothing typed finds nothing — the list under the field is the followed leagues, not a catalogue.
/// </summary>
public static class SportsScreenFind
{
    /// <summary>A screenful; the viewer narrows further by typing.</summary>
    public const int MostShown = 30;

    public static IReadOnlyList<FindRow> Matches(
        IEnumerable<(League League, Team Team)> candidates, string query, IReadOnlySet<TeamId> followed)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(followed);
        var words = query.Trim();
        if (words.Length == 0)
        {
            return [];
        }

        return candidates
            .Where(candidate => candidate.Team.Name.Contains(words, StringComparison.CurrentCultureIgnoreCase)
                || candidate.League.Name.Contains(words, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(candidate => candidate.League.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.Team.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MostShown)
            .Select(candidate => new FindRow(candidate.Team, candidate.League.Id, candidate.League.Name, followed.Contains(candidate.Team.Id)))
            .ToList();
    }
}
