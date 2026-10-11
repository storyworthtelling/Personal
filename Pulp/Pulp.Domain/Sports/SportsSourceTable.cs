namespace Pulp.Domain.Sports;

/// <summary>The ids of the two sources (D208). Stored nowhere; the table below and the adapters share them.</summary>
public static class SportsSourceIds
{
    public const string MlbStats = "mlb-stats";

    public const string Espn = "espn";
}

/// <summary>A source as About names it: what it is and the terms Pulp reads it under (D140).</summary>
public sealed record SportsSourceInfo(string Id, string Name, string Terms);

/// <summary>One league and the source it is read from.</summary>
public sealed record SportsLeagueEntry(LeagueId League, string Name, string SourceId);

/// <summary>
/// Which source each league is read from (D208: league feeds where their terms permit, ESPN's JSON
/// for the rest) — the table Settings → About prints (D140). Pure; the adapters are chosen from it.
/// </summary>
public sealed class SportsSourceTable
{
    private readonly Dictionary<LeagueId, SportsLeagueEntry> byLeague;
    private readonly Dictionary<string, SportsSourceInfo> bySource;

    public SportsSourceTable(IReadOnlyList<SportsSourceInfo> sources, IReadOnlyList<SportsLeagueEntry> leagues)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(leagues);
        bySource = sources.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        byLeague = [];
        foreach (var entry in leagues)
        {
            if (!bySource.ContainsKey(entry.SourceId))
            {
                throw new ArgumentException($"League '{entry.League}' names the source '{entry.SourceId}', which the table does not list.", nameof(leagues));
            }

            if (!byLeague.TryAdd(entry.League, entry))
            {
                throw new ArgumentException($"League '{entry.League}' is listed twice; it is read from one source.", nameof(leagues));
            }
        }

        Sources = sources;
        Leagues = leagues;
    }

    public IReadOnlyList<SportsSourceInfo> Sources { get; }

    public IReadOnlyList<SportsLeagueEntry> Leagues { get; }

    /// <summary>The source id for a league, or null when the league is not in the table.</summary>
    public string? SourceOf(LeagueId league) => byLeague.TryGetValue(league, out var entry) ? entry.SourceId : null;

    /// <summary>One line per league for About: "League — read from Source (terms)".</summary>
    public IReadOnlyList<string> AboutLines() =>
        Leagues.Select(entry =>
        {
            var source = bySource[entry.SourceId];
            return $"{entry.Name} — read from {source.Name} ({source.Terms})";
        }).ToList();

    /// <summary>
    /// The shipped table. MLB's own feed carries an explicit notice permitting individual,
    /// non-commercial, non-bulk use (D206, D208); every other league comes from ESPN's unofficial JSON,
    /// which publishes no terms and can change without notice (finding §1.2).
    /// </summary>
    public static SportsSourceTable Default { get; } = new(
        [
            new SportsSourceInfo(SportsSourceIds.MlbStats, "MLB Stats API", "MLB's notice permits individual, non-commercial use"),
            new SportsSourceInfo(SportsSourceIds.Espn, "ESPN's public JSON", "unofficial, no published terms"),
        ],
        [
            new SportsLeagueEntry(new LeagueId("mlb"), "MLB", SportsSourceIds.MlbStats),
            new SportsLeagueEntry(new LeagueId("nfl"), "NFL", SportsSourceIds.Espn),
            new SportsLeagueEntry(new LeagueId("nhl"), "NHL", SportsSourceIds.Espn),
            new SportsLeagueEntry(new LeagueId("nba"), "NBA", SportsSourceIds.Espn),
            new SportsLeagueEntry(new LeagueId("wnba"), "WNBA", SportsSourceIds.Espn),
            new SportsLeagueEntry(new LeagueId("nwsl"), "NWSL", SportsSourceIds.Espn),
            new SportsLeagueEntry(new LeagueId("afl"), "AFL", SportsSourceIds.Espn),
        ]);
}
