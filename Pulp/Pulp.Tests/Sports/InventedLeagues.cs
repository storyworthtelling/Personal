using Pulp.Domain.Sports;

namespace Pulp.Tests.Sports;

/// <summary>
/// Invented leagues and teams for the sports rules — the same ones mockup 7 draws. None of them is
/// a real competition, club or broadcaster.
/// </summary>
internal static class InventedLeagues
{
    public static readonly DateTimeOffset Now = new(2026, 1, 20, 12, 0, 0, TimeSpan.FromHours(-5));

    public static readonly LeagueId Coastal = new("cfl-invented");
    public static readonly LeagueId Southern = new("srl-invented");
    public static readonly LeagueId Lakeshore = new("lwh-invented");

    public static readonly Team Gulls = new(new TeamId("harbor-gulls"), "Harbor Gulls", "badge://harbor-gulls");
    public static readonly Team Foxes = new(new TeamId("ridge-foxes"), "Ridge Foxes", "badge://ridge-foxes");
    public static readonly Team Herons = new(new TeamId("bay-herons"), "Bay Herons", "badge://bay-herons");
    public static readonly Team Otters = new(new TeamId("cove-otters"), "Cove Otters", "badge://cove-otters");

    public static readonly GameId GullsRecording = new("g-001");
    public static readonly GameId GullsNextPlayoff = new("g-002");
    public static readonly GameId HeronsNextPlayoff = new("g-003");
    public static readonly GameId HeronsWatched = new("g-004");
    public static readonly GameId CoastalBowl = new("g-bowl");

    public static League CoastalLeague(SeasonPhase phase = SeasonPhase.Postseason) =>
        new(Coastal, "Coastal Football League", "badge://cfl", phase, new DateOnly(2026, 2, 8), null);

    /// <summary>
    /// The Coastal league mid-playoffs: the Gulls have an unwatched playoff recording, a next playoff
    /// fixture whose existence says who won it, and the league's standings, highlights and bowl.
    /// </summary>
    public static LeagueFeed CoastalFeed(SeasonPhase phase = SeasonPhase.Postseason) => new(
        CoastalLeague(phase),
        [Gulls, Foxes, Herons, Otters],
        [
            new Game(GullsRecording, Foxes, Gulls, Now.AddDays(-2), GameStage.Playoffs, "Gridline TV · channel 7"),
            new Game(GullsNextPlayoff, Herons, Gulls, Now.AddDays(5), GameStage.Playoffs, "Gridline TV · channel 7"),
            new Game(HeronsWatched, Herons, Otters, Now.AddDays(-3), GameStage.Playoffs, "Gridline TV · channel 9"),
            new Game(HeronsNextPlayoff, Herons, Otters, Now.AddDays(4), GameStage.Playoffs, "Gridline TV · channel 9"),
        ],
        [Herons, Gulls, Foxes, Otters],
        [new Highlight("video-one", "Week in review")],
        new Championship(CoastalBowl, "The Coastal Bowl", Now.AddDays(19), [Herons, Gulls]));
}
