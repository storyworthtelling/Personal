namespace Pulp.Domain.Sports;

/// <summary>
/// D330: the games an open league keeps reachable after they are watched, so their highlights stay one tap
/// away. A game qualifies when it involves the team, falls inside the schedule window already fetched
/// (<see cref="SportsWindow.RecordingDays"/>), is in the watched set the evidence produced
/// (<see cref="RecordedGames.Watched"/>) and is over by the clock alone (<see cref="GameOver"/>), newest first.
/// Only <see cref="SpoilerHold.Present"/> calls it, and only for a league with nothing held: a game that is
/// over is never shown in a held league (D242), and the feed's score and status are never read (G15).
/// </summary>
public static class EarlierGames
{
    public static IReadOnlyList<GameLine> For(
        LeagueFeed feed, TeamId team, IReadOnlyCollection<GameId> watched, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(watched);
        var oldest = now.AddDays(-SportsWindow.RecordingDays);
        return feed.Schedule
            .Where(game => game.Involves(team) && game.StartsAt >= oldest && watched.Contains(game.Id))
            .Where(game => GameOver.IsOver(feed.League.Id, game.StartsAt, now))
            .OrderByDescending(game => game.StartsAt)
            .ThenBy(game => game.Id.Value, StringComparer.OrdinalIgnoreCase)
            .Select(game => new GameLine(game.Id, game.Home, game.Away, game.StartsAt, game.Stage, game.Channel, Recorded: false, Over: true))
            .ToList();
    }
}
