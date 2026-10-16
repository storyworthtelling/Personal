namespace Pulp.Domain.Sports;

/// <summary>
/// Whether a game is over, decided by the clock and by nothing else: the game's start plus a typical length for
/// the league's sport. No score, winner, record or feed status flag takes part and none can (G15): the
/// signature takes a league and two moments. The lengths are un-evidenced estimates; a league the table does not
/// know takes the longest, so an unknown game is called over late, never early.
/// </summary>
public static class GameOver
{
    /// <summary>Typical length from the start to the last whistle, by league id (compared without case).</summary>
    public static IReadOnlyDictionary<LeagueId, TimeSpan> Lengths { get; } = new Dictionary<LeagueId, TimeSpan>
    {
        [new LeagueId("nfl")] = TimeSpan.FromHours(4),
        [new LeagueId("mlb")] = TimeSpan.FromHours(4),
        [new LeagueId("nba")] = TimeSpan.FromHours(3),
        [new LeagueId("nhl")] = TimeSpan.FromHours(3),
        [new LeagueId("wnba")] = TimeSpan.FromHours(3),
        [new LeagueId("mls")] = TimeSpan.FromMinutes(150),
        [new LeagueId("nwsl")] = TimeSpan.FromMinutes(150),
        [new LeagueId("pwhl")] = TimeSpan.FromHours(3),
        [new LeagueId("pwbl")] = TimeSpan.FromHours(3),
    };

    public static TimeSpan LengthOf(LeagueId league) =>
        Lengths.TryGetValue(league, out var length) ? length : Lengths.Values.Max();

    public static bool IsOver(LeagueId league, DateTimeOffset startsAt, DateTimeOffset now) =>
        now - startsAt >= LengthOf(league);
}
