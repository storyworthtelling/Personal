namespace Pulp.Domain.Sports;

/// <summary>
/// The spoiler rule for sport (D55, D70, D237, D242, D183). While a team this viewer follows has an
/// unwatched recording, its league is <b>held</b>: standings, highlights, the bracket and any fixture
/// whose existence would reveal a result are absent, and the championship shows its name and date
/// only. The Spoilers tag on the league's line peeks once (<see cref="SpoilerPeeks"/>); the hold
/// returns afterwards, and lifts only when the recording is watched.
///
/// Fails safe: anything the rule is unsure of is left out of a held league rather than shown.
/// </summary>
public static class SpoilerHold
{
    /// <summary>
    /// A playoff or championship fixture: who is in it says who won the round before (D237). A
    /// regular-season or preseason fixture is on the schedule whatever happened last week.
    /// </summary>
    public static bool Reveals(GameStage stage) => stage is GameStage.Playoffs or GameStage.Championship;

    /// <summary>
    /// True while any team in <paramref name="followed"/> that plays in this league has a game that
    /// has started and is in <paramref name="unwatchedRecordings"/>.
    /// </summary>
    public static bool IsHeld(
        LeagueFeed feed,
        IReadOnlyCollection<TeamId> followed,
        IReadOnlyCollection<GameId> unwatchedRecordings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(feed);
        return FollowedTeams(feed, followed)
            .Any(team => UnwatchedRecordings(feed, team.Id, unwatchedRecordings, now).Count > 0);
    }

    /// <summary>
    /// The league as this viewer sees it now. A held league comes back as <see cref="HeldLeague"/>
    /// unless <paramref name="peeking"/>, which the caller sets only after spending a peek.
    /// </summary>
    public static LeagueSheet Present(
        LeagueFeed feed,
        IReadOnlyCollection<TeamId> followed,
        IReadOnlyCollection<GameId> unwatchedRecordings,
        DateTimeOffset now,
        bool peeking,
        IReadOnlyCollection<GameId>? watchedGames = null)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var league = feed.League;
        var line = new LeagueLine(league.Id, league.Name, league.BadgeArt, league.Phase, league.NextDate);
        var teams = FollowedTeams(feed, followed);
        var held = IsHeld(feed, followed, unwatchedRecordings, now);

        if (held && !peeking)
        {
            var heldTeams = teams
                .Select(team => new TeamGames(team, HeldGames(feed, team.Id, unwatchedRecordings, now)))
                .ToList();
            var heldChampionship = feed.Championship is null
                ? null
                : new HeldChampionship(feed.Championship.Name, feed.Championship.StartsAt);
            return new HeldLeague(line, heldTeams, heldChampionship);
        }

        var openTeams = teams
            .Select(team => new TeamGames(team, OpenGames(feed, team.Id, unwatchedRecordings, now)))
            .ToList();
        var championship = feed.Championship is null
            ? null
            : new ChampionshipLine(feed.Championship.Name, feed.Championship.StartsAt, feed.Championship.Teams);
        // D330: the Earlier fold belongs to a league with nothing held. A peek is a held league and gets none.
        var earlier = held || watchedGames is null
            ? []
            : teams
                .Select(team => new TeamEarlier(team.Id, EarlierGames.For(feed, team.Id, watchedGames, now)))
                .Where(entry => entry.Games.Count > 0)
                .ToList();
        return new OpenLeague(line, openTeams, championship, feed.Standings, feed.Highlights, IsPeek: held) { Earlier = earlier };
    }

    /// <summary>The followed teams that play in this league, by name.</summary>
    internal static List<Team> FollowedTeams(LeagueFeed feed, IReadOnlyCollection<TeamId> followed)
    {
        ArgumentNullException.ThrowIfNull(followed);
        return feed.Teams
            .Where(team => followed.Contains(team.Id))
            .OrderBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<Game> UnwatchedRecordings(
        LeagueFeed feed, TeamId team, IReadOnlyCollection<GameId> unwatchedRecordings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(unwatchedRecordings);
        return feed.Schedule
            .Where(game => game.Involves(team) && game.StartsAt <= now && unwatchedRecordings.Contains(game.Id))
            .OrderBy(game => game.StartsAt)
            .ToList();
    }

    private static Game? NextGame(LeagueFeed feed, TeamId team, DateTimeOffset now) =>
        feed.Schedule
            .Where(game => game.Involves(team) && game.StartsAt > now)
            .OrderBy(game => game.StartsAt)
            .FirstOrDefault();

    /// <summary>
    /// D236: the unwatched recordings, then the next game. A recording whose game is over by the clock is
    /// marked so (the finished list, see the ordering in <see cref="SportsScreenOrder"/>); a watched game is
    /// not an unwatched recording and so never reaches the list.
    /// </summary>
    private static List<GameLine> OpenGames(
        LeagueFeed feed, TeamId team, IReadOnlyCollection<GameId> unwatchedRecordings, DateTimeOffset now)
    {
        var lines = UnwatchedRecordings(feed, team, unwatchedRecordings, now)
            .Select(game => Line(game, recorded: true, over: GameOver.IsOver(feed.League.Id, game.StartsAt, now)))
            .ToList();
        var next = NextGame(feed, team, now);
        if (next is not null)
        {
            lines.Add(Line(next, recorded: false));
        }

        return lines;
    }

    /// <summary>
    /// Under the hold: the recordings still show, since the viewer knows they exist — but only up to
    /// the first revealing one, because a second playoff recording exists only because the first was
    /// won. The next game shows only when it reveals nothing.
    /// </summary>
    private static List<GameLine> HeldGames(
        LeagueFeed feed, TeamId team, IReadOnlyCollection<GameId> unwatchedRecordings, DateTimeOffset now)
    {
        var lines = new List<GameLine>();
        foreach (var game in UnwatchedRecordings(feed, team, unwatchedRecordings, now))
        {
            lines.Add(Line(game, recorded: true));
            if (Reveals(game.Stage))
            {
                return lines;
            }
        }

        var next = NextGame(feed, team, now);
        if (next is not null && !Reveals(next.Stage))
        {
            lines.Add(Line(next, recorded: false));
        }

        return lines;
    }

    // A held league's lines are built without `over`: a held sheet never says a game is over.
    private static GameLine Line(Game game, bool recorded, bool over = false) =>
        new(game.Id, game.Home, game.Away, game.StartsAt, game.Stage, game.Channel, recorded, over);
}

/// <summary>
/// The one peek (D183, D242). Tapping the Spoilers tag grants a peek at that league; drawing the
/// league spends it, so the next draw is held again. Kept per league: a peek at one league opens no
/// other.
/// </summary>
public sealed class SpoilerPeeks
{
    // Tapped on the UI thread and spent on a background paint, so every access takes the lock.
    private readonly object gate = new();
    private readonly HashSet<LeagueId> granted = [];

    public void Tap(LeagueId league)
    {
        lock (gate)
        {
            granted.Add(league);
        }
    }

    /// <summary>True once per tap, then false until the tag is tapped again.</summary>
    public bool Spend(LeagueId league)
    {
        lock (gate)
        {
            return granted.Remove(league);
        }
    }
}
