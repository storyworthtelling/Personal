namespace Pulp.Domain.Sports;

/// <summary>One line of the standings: a place in the order and the team in it — rank only, nothing numeric beside it.</summary>
public sealed record StandingRow(int Rank, string Name, string BadgeArt, bool Followed);

/// <summary>The bracket page: the championship and the playoff games the sheet carries.</summary>
public sealed record BracketPlan(ChampionshipRow? Championship, IReadOnlyList<GameRow> Playoffs);

/// <summary>
/// The pages behind an open league's links (standings, bracket, schedule — no mockup, D272, built from
/// the same Paper anatomy as the rest). Every builder takes an <see cref="OpenLeague"/>, never a
/// <see cref="HeldLeague"/> or a <see cref="LeagueSheet"/>: a held league has no page to build, and the
/// type system says so rather than a flag.
/// </summary>
public static class SportsScreenPages
{
    public static IReadOnlyList<StandingRow> Standings(OpenLeague league)
    {
        ArgumentNullException.ThrowIfNull(league);
        var followed = league.Teams.Select(team => team.Team.Id).ToHashSet();
        return league.Standings
            .Select((team, index) => new StandingRow(index + 1, team.Name, team.BadgeArt, followed.Contains(team.Id)))
            .ToList();
    }

    public static BracketPlan Bracket(OpenLeague league, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(league);
        var champion = league.Championship is { } champ ? SportsScreenPlan.ChampionshipOf(champ, zone) : null;
        var games = PlayoffGames(league).Select(game => GameRow.From(game, now, zone, HighlightsOffer.Allowed(league))).ToList();
        return new BracketPlan(champion, games);
    }

    /// <summary>Every game the sheet carries, once each, in time order.</summary>
    public static IReadOnlyList<GameRow> Schedule(OpenLeague league, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(league);
        return AllGames(league).Select(game => GameRow.From(game, now, zone, HighlightsOffer.Allowed(league))).ToList();
    }

    /// <summary>The playoff and championship games among those the sheet carries.</summary>
    internal static IReadOnlyList<GameLine> PlayoffGames(OpenLeague league) =>
        AllGames(league).Where(game => SpoilerHold.Reveals(game.Stage)).ToList();

    private static List<GameLine> AllGames(OpenLeague league) =>
        league.Teams
            .SelectMany(team => team.Games)
            .DistinctBy(game => game.Id)
            .OrderBy(game => game.StartsAt)
            .ThenBy(game => game.Id.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
