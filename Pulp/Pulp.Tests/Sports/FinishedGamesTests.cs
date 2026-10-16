using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// The Sports page lists the finished games of followed teams in an open league (newest first), so a game
/// that is over can be marked watched, played, or sent to its highlights. A held league lists none of that:
/// it never shows that a game is over, and the spoiler hold stays the authority (D242). A watched game
/// drops off, as a recording does (D55).
/// </summary>
public class FinishedGamesTests
{
    private static readonly TeamId[] FollowsGulls = [Gulls.Id];
    private static readonly ViewerId Viewer = new("viewer-one");

    private static Game Regular(string id, Team home, Team away, TimeSpan fromNow) =>
        new(new GameId(id), home, away, Now + fromNow, GameStage.RegularSeason, "Gridline TV");

    private static LeagueFeed Feed(params Game[] games) => new(
        CoastalLeague(SeasonPhase.RegularSeason),
        [Gulls, Foxes, Herons, Otters],
        games,
        [Herons, Gulls, Foxes, Otters],
        [new Highlight("video-one", "Week in review")],
        null);

    private static readonly Game LastWeek = Regular("f-week", Foxes, Gulls, TimeSpan.FromDays(-7));
    private static readonly Game ThreeDays = Regular("f-three", Herons, Gulls, TimeSpan.FromDays(-3));
    private static readonly Game Yesterday = Regular("f-day", Foxes, Gulls, TimeSpan.FromDays(-1));
    private static readonly Game InProgress = Regular("f-live", Herons, Gulls, TimeSpan.FromHours(-1));
    private static readonly Game Upcoming = Regular("f-next", Foxes, Gulls, TimeSpan.FromDays(2));

    private static OpenLeague Open(LeagueFeed feed, params GameId[] unwatched) =>
        Assert.IsType<OpenLeague>(SpoilerHold.Present(feed, FollowsGulls, unwatched, Now, peeking: true));

    private static List<GameLine> GullsLines(LeagueSheet sheet) => sheet switch
    {
        OpenLeague open => open.Teams.Single().Games.ToList(),
        HeldLeague held => held.Teams.Single().Games.ToList(),
        _ => throw new InvalidOperationException("Neither sheet."),
    };

    [Fact]
    public void AnOpenLeagueMarksEveryListedGameThatIsOverByTheClockAlone()
    {
        var feed = Feed(LastWeek, Yesterday, InProgress, Upcoming);
        var lines = GullsLines(Open(feed, LastWeek.Id, Yesterday.Id, InProgress.Id));

        Assert.True(lines.Single(line => line.Id == LastWeek.Id).Over);
        Assert.True(lines.Single(line => line.Id == Yesterday.Id).Over);
        Assert.False(lines.Single(line => line.Id == InProgress.Id).Over);
        Assert.False(lines.Single(line => line.Id == Upcoming.Id).Over);
    }

    [Fact]
    public void AnOpenLeagueWithNothingUnwatchedListsNoFinishedGame()
    {
        var feed = Feed(LastWeek, Yesterday, Upcoming);

        var sheet = Assert.IsType<OpenLeague>(SpoilerHold.Present(feed, FollowsGulls, [], Now, peeking: false));
        var lines = GullsLines(sheet);

        Assert.False(sheet.IsPeek);
        Assert.Equal([Upcoming.Id], lines.Select(line => line.Id));
        Assert.DoesNotContain(lines, line => line.Over);
    }

    [Fact]
    public void AWatchedGameDropsOffTheFinishedList()
    {
        var feed = Feed(LastWeek, Yesterday, Upcoming);

        var before = GullsLines(Open(feed, LastWeek.Id, Yesterday.Id));
        var after = GullsLines(Open(feed, Yesterday.Id));

        Assert.Contains(before, line => line.Id == LastWeek.Id);
        Assert.DoesNotContain(after, line => line.Id == LastWeek.Id);
        Assert.Contains(after, line => line.Id == Yesterday.Id);
    }

    [Fact]
    public void ARowForAFinishedGameIsTheGenericRowWithMarkPlayAndHighlights()
    {
        var feed = Feed(Yesterday);
        var team = Open(feed, Yesterday.Id).Teams.Single();

        var row = GameRow.From(team.Games.Single(), Now, TimeZoneInfo.Utc, highlights: true);

        Assert.Equal("Harbor Gulls at Ridge Foxes", row.Title);
        Assert.True(row.Recorded);
        Assert.Equal("Gridline TV", row.Channel);
        Assert.True(row.HighlightsOffered);
        Assert.StartsWith("Highlights · ", row.HighlightsLabel);
    }

    [Fact]
    public void FinishedGamesRunNewestFirstAfterAnyGameStillInProgressAndBeforeTheNextGame()
    {
        var feed = Feed(LastWeek, ThreeDays, Yesterday, InProgress, Upcoming);
        var sheet = Open(feed, LastWeek.Id, ThreeDays.Id, Yesterday.Id, InProgress.Id);

        var ordered = SportsScreenOrder.Games(sheet.Teams.Single().Games);

        Assert.Equal([InProgress.Id.Value, Yesterday.Id.Value, ThreeDays.Id.Value, LastWeek.Id.Value, Upcoming.Id.Value], ordered.Select(line => line.Id.Value));
    }

    // ---- the hold stays the authority ----

    private static LeagueFeed HeldFeed() => new(
        CoastalLeague(),
        [Gulls, Foxes, Herons, Otters],
        [
            new Game(GullsRecording, Foxes, Gulls, Now.AddDays(-2), GameStage.Playoffs, "Gridline TV · channel 7"),
            new Game(new GameId("p-later"), Herons, Gulls, Now.AddDays(-1), GameStage.Playoffs, "Gridline TV · channel 7"),
            new Game(new GameId("p-earlier"), Foxes, Gulls, Now.AddDays(-9), GameStage.Playoffs, "Gridline TV · channel 7"),
            new Game(new GameId("r-old"), Otters, Gulls, Now.AddDays(-12), GameStage.RegularSeason, "Gridline TV"),
            new Game(GullsNextPlayoff, Herons, Gulls, Now.AddDays(5), GameStage.Playoffs, "Gridline TV · channel 7"),
        ],
        [Herons, Gulls, Foxes, Otters],
        [new Highlight("video-one", "Week in review")],
        new Championship(CoastalBowl, "The Coastal Bowl", Now.AddDays(19), [Herons, Gulls]));

    [Fact]
    public void AHeldLeagueListsNoFinishedGameAndNeverSaysAnyIsOver()
    {
        // Three finished games sit in the window; only the viewer's own unwatched recording is held on the sheet, as before.
        var sheet = SpoilerHold.Present(HeldFeed(), FollowsGulls, [GullsRecording], Now, peeking: false);

        var held = Assert.IsType<HeldLeague>(sheet);
        var lines = GullsLines(held);
        Assert.Equal([GullsRecording], lines.Select(line => line.Id));
        Assert.All(lines, line => Assert.False(line.Over));
    }

    [Fact]
    public void AHeldLeaguePlanOffersNoFinishedRowNoHighlightsAndNoOverText()
    {
        var feedLeague = HeldFeed().League;
        var page = new SportsPage([SpoilerHold.Present(HeldFeed(), FollowsGulls, [GullsRecording], Now, peeking: false)], LeagueFold.Starting([feedLeague]));
        var plan = SportsScreenPlan.For(page, FoldMemory.Empty, Now, TimeZoneInfo.Utc).Single();

        var rows = plan.Teams.SelectMany(team => team.Games).ToList();
        Assert.Equal([GullsRecording], rows.Select(row => row.Id));
        Assert.All(rows, row => Assert.False(row.HighlightsOffered));
        Assert.Empty(plan.Highlights);
    }

    [Fact]
    public void PeekingStillShowsTheLeagueOnceAndThenTheHoldComesBack()
    {
        var feed = HeldFeed();
        var follows = new[] { new SportsFollow(Viewer, Gulls.Id) };
        var peeks = new SpoilerPeeks();
        var sittings = Array.Empty<WatchEvent>();

        var held = SportsPage.Build([feed], follows, Viewer, sittings, Now, peeks, FoldMemory.Empty);
        peeks.Tap(Coastal);
        var peeked = SportsPage.Build([feed], follows, Viewer, sittings, Now, peeks, FoldMemory.Empty);
        var after = SportsPage.Build([feed], follows, Viewer, sittings, Now, peeks, FoldMemory.Empty);

        Assert.IsType<HeldLeague>(held.Sheets.Single());
        var open = Assert.IsType<OpenLeague>(peeked.Sheets.Single());
        Assert.True(open.IsPeek);
        Assert.IsType<HeldLeague>(after.Sheets.Single());
        Assert.All(GullsLines(held.Sheets.Single()), line => Assert.False(line.Over));
    }

    [Fact]
    public void ALeagueHeldByOneTeamHidesTheOtherTeamsFinishedGamesToo()
    {
        var feed = HeldFeed();
        var sheet = SpoilerHold.Present(feed, [Gulls.Id, Otters.Id], [GullsRecording], Now, peeking: false);

        var held = Assert.IsType<HeldLeague>(sheet);
        var otters = held.Teams.Single(team => team.Team.Id == Otters.Id);
        Assert.Empty(otters.Games);
    }
}
