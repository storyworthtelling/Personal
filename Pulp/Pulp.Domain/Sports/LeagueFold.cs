namespace Pulp.Domain.Sports;

/// <summary>
/// What the viewer has opened or closed on the Sports tab, which wins over the starting rule from
/// then on (D241: "what the viewer opens stays open"). Immutable; the head persists it.
/// A team is remembered within its league, since one team can play in several competitions (D185).
/// </summary>
public sealed class FoldMemory
{
    private readonly IReadOnlyDictionary<string, bool> choices;

    private FoldMemory(IReadOnlyDictionary<string, bool> choices) => this.choices = choices;

    public static FoldMemory Empty { get; } = new(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Every remembered choice, keyed as <see cref="LeagueKey"/> and <see cref="TeamKey"/> write it.</summary>
    public IReadOnlyDictionary<string, bool> Choices => choices;

    public static FoldMemory From(IReadOnlyDictionary<string, bool> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return new FoldMemory(new Dictionary<string, bool>(choices, StringComparer.OrdinalIgnoreCase));
    }

    public static string LeagueKey(LeagueId league) => "league:" + league.Value;

    public static string TeamKey(LeagueId league, TeamId team) => "team:" + league.Value + ":" + team.Value;

    /// <summary>
    /// A team's "Earlier" fold (D330). Its own key, so folding the team never opens or closes it. It rides the
    /// same frozen preferences file and <c>fold:</c> prefix as every other choice, so no new stored name exists.
    /// </summary>
    public static string EarlierKey(LeagueId league, TeamId team) => "earlier:" + league.Value + ":" + team.Value;

    public FoldMemory WithEarlier(LeagueId league, TeamId team, bool open) => With(EarlierKey(league, team), open);

    public FoldMemory WithLeague(LeagueId league, bool open) => With(LeagueKey(league), open);

    public FoldMemory WithTeam(LeagueId league, TeamId team, bool open) => With(TeamKey(league, team), open);

    public bool? Remembered(string key) => choices.TryGetValue(key, out var open) ? open : null;

    private FoldMemory With(string key, bool open) =>
        new(new Dictionary<string, bool>(choices, StringComparer.OrdinalIgnoreCase) { [key] = open });
}

/// <summary>
/// D241: leagues and teams fold. Only the league closest to its season's end starts open, and within
/// it only the teams with an unwatched recording; everything else starts folded. A choice the viewer
/// made (<see cref="FoldMemory"/>) overrides the starting rule.
/// </summary>
public sealed class LeagueFold
{
    private readonly LeagueId? startsOpen;
    private readonly FoldMemory memory;

    private LeagueFold(LeagueId? startsOpen, FoldMemory memory)
    {
        this.startsOpen = startsOpen;
        this.memory = memory;
    }

    public static LeagueFold Starting(IEnumerable<League> leagues, FoldMemory? memory = null) =>
        new(LeagueOrder.ClosestToEnd(leagues)?.Id, memory ?? FoldMemory.Empty);

    public bool IsLeagueOpen(LeagueId league) =>
        memory.Remembered(FoldMemory.LeagueKey(league)) ?? league == startsOpen;

    public bool IsTeamOpen(LeagueId league, TeamId team, bool hasUnwatchedRecording) =>
        memory.Remembered(FoldMemory.TeamKey(league, team)) ?? (league == startsOpen && hasUnwatchedRecording);
}
