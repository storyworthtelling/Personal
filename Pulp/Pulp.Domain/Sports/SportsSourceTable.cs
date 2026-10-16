namespace Pulp.Domain.Sports;

/// <summary>The ids of the two sources (D208). Stored nowhere; the table below and the adapters share them.</summary>
public static class SportsSourceIds
{
    public const string MlbStats = "mlb-stats";

    public const string Espn = "espn";

    /// <summary>The PWHL's own "Sync to Your Calendar" file (D354).</summary>
    public const string PwhlCalendar = "pwhl-calendar";
}

/// <summary>A source as About names it: what it is and the terms Pulp reads it under (D140).</summary>
public sealed record SportsSourceInfo(string Id, string Name, string Terms);

/// <summary>
/// One league and the source it is read from. A league with no public, key-free, score-free schedule source
/// has a null <see cref="SourceId"/> and says why in <see cref="NoSourceNote"/> (D340): it is in the table so
/// About can state that plainly, and it is never asked about, since nothing could answer.
/// </summary>
public sealed record SportsLeagueEntry(LeagueId League, string Name, string? SourceId, string? NoSourceNote = null)
{
    public bool IsSourced => SourceId is not null;
}

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
            if (entry.SourceId is null)
            {
                if (string.IsNullOrWhiteSpace(entry.NoSourceNote))
                {
                    throw new ArgumentException($"League '{entry.League}' has no source and no sentence saying why; About has to say.", nameof(leagues));
                }
            }
            else
            {
                if (!bySource.ContainsKey(entry.SourceId))
                {
                    throw new ArgumentException($"League '{entry.League}' names the source '{entry.SourceId}', which the table does not list.", nameof(leagues));
                }

                if (entry.NoSourceNote is not null)
                {
                    throw new ArgumentException($"League '{entry.League}' has a source and also a no-source note; it is one or the other.", nameof(leagues));
                }
            }

            if (!byLeague.TryAdd(entry.League, entry))
            {
                throw new ArgumentException($"League '{entry.League}' is listed twice; it is read from one source.", nameof(leagues));
            }
        }

        Sources = sources;
        All = leagues;
        Leagues = leagues.Where(entry => entry.IsSourced).ToList();
        Unsourced = leagues.Where(entry => !entry.IsSourced).ToList();
    }

    public IReadOnlyList<SportsSourceInfo> Sources { get; }

    /// <summary>The leagues that have a source, in table order: the ones that can be asked about, browsed and followed.</summary>
    public IReadOnlyList<SportsLeagueEntry> Leagues { get; }

    /// <summary>The leagues the owner follows that no source serves yet (D340), in table order.</summary>
    public IReadOnlyList<SportsLeagueEntry> Unsourced { get; }

    /// <summary>Every league, sourced or not, in the table's order: the owner's watched leagues first.</summary>
    public IReadOnlyList<SportsLeagueEntry> All { get; }
    /// <summary>The source id for a league, or null when the league is not in the table.</summary>
    public string? SourceOf(LeagueId league) => byLeague.TryGetValue(league, out var entry) ? entry.SourceId : null;

    /// <summary>
    /// One line per league for About: "League — read from Source (terms)", or for a league no source serves,
    /// "League — no schedule source yet: why".
    /// </summary>
    public IReadOnlyList<string> AboutLines() =>
        All.Select(entry =>
        {
            if (entry.SourceId is null)
            {
                return $"{entry.Name} — no schedule source yet: {entry.NoSourceNote}";
            }

            var source = bySource[entry.SourceId];
            return $"{entry.Name} — read from {source.Name} ({source.Terms})";
        }).ToList();

    /// <summary>
    /// The shipped table, the owner's watched leagues first (D340): MLB, NFL, NHL, MLS, NWSL, PWHL, PWBL, the Olympics,
    /// then the ones kept but not followed. MLB's own feed carries an explicit notice permitting individual,
    /// non-commercial, non-bulk use (D206, D208); the PWHL is read only from the calendar file its own site offers for syncing (D354); every other league comes from ESPN's unofficial JSON,
    /// which publishes no terms and can change without notice (finding §1.2).
    /// </summary>
    public static SportsSourceTable Default { get; } = new(
        [
            new SportsSourceInfo(SportsSourceIds.MlbStats, "MLB Stats API", "MLB's notice permits individual, non-commercial use"),
            new SportsSourceInfo(SportsSourceIds.Espn, "ESPN's public JSON", "unofficial, no published terms"),
            new SportsSourceInfo(
                SportsSourceIds.PwhlCalendar, "the PWHL's own calendar file",
                "the file its site offers for syncing to a calendar; its terms forbid other automated reading, so nothing else of the site is touched"),
        ],
        [
            new SportsLeagueEntry(new LeagueId("mlb"), "MLB", SportsSourceIds.MlbStats),
            new SportsLeagueEntry(new LeagueId("nfl"), "NFL", SportsSourceIds.Espn),
            new SportsLeagueEntry(new LeagueId("nhl"), "NHL", SportsSourceIds.Espn),
            new SportsLeagueEntry(new LeagueId("mls"), "MLS", SportsSourceIds.Espn),
            new SportsLeagueEntry(new LeagueId("nwsl"), "NWSL", SportsSourceIds.Espn),
            new SportsLeagueEntry(new LeagueId("pwhl"), "PWHL", SportsSourceIds.PwhlCalendar),
            new SportsLeagueEntry(
                new LeagueId("pwbl"), "PWBL", null,
                "no public schedule feed could be reached or found"),
            new SportsLeagueEntry(
                new LeagueId("olympics"), "Olympics", null,
                "the Games' schedule is by sport and day, not by team, and the official site refuses automated reading"),
            new SportsLeagueEntry(new LeagueId("nba"), "NBA", SportsSourceIds.Espn),
            new SportsLeagueEntry(new LeagueId("wnba"), "WNBA", SportsSourceIds.Espn),
            new SportsLeagueEntry(new LeagueId("afl"), "AFL", SportsSourceIds.Espn),
        ]);
}
