using Pulp.Domain.Actions;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// D330: an open league keeps the finished games of followed teams reachable after they are watched, under
/// a quiet "Earlier" fold, newest first, from the schedule window already fetched. A held league has none
/// of it and never says a game is over (D242); a peek is unchanged (D183).
/// </summary>
public class EarlierGamesTests
{
    private static readonly ViewerId Viewer = new("viewer-one");
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    private static readonly TeamId[] FollowsGulls = [Gulls.Id];

    private static Game Regular(string id, Team home, Team away, TimeSpan fromNow) =>
        new(new GameId(id), home, away, Now + fromNow, GameStage.RegularSeason, "Gridline TV");

    private static readonly Game LastWeek = Regular("e-week", Foxes, Gulls, TimeSpan.FromDays(-7));
    private static readonly Game ThreeDays = Regular("e-three", Herons, Gulls, TimeSpan.FromDays(-3));
    private static readonly Game Yesterday = Regular("e-day", Foxes, Gulls, TimeSpan.FromDays(-1));
    private static readonly Game TooOld = Regular("e-old", Otters, Gulls, TimeSpan.FromDays(-20));
    private static readonly Game InProgress = Regular("e-live", Herons, Gulls, TimeSpan.FromHours(-1));
    private static readonly Game Upcoming = Regular("e-next", Foxes, Gulls, TimeSpan.FromDays(2));
    private static readonly Game OtherTeams = Regular("e-other", Herons, Otters, TimeSpan.FromDays(-2));

    private static LeagueFeed Feed(params Game[] games) => new(
        CoastalLeague(SeasonPhase.RegularSeason),
        [Gulls, Foxes, Herons, Otters],
        games,
        [Herons, Gulls, Foxes, Otters],
        [new Highlight("video-one", "Week in review")],
        null);

    private static WatchEvent Watch(Game game, WatchSource how = WatchSource.MarkedWatched, int hoursAgo = 2) =>
        new(new TitleId(game.Id.Value), WatchedKind.Game, Now.AddHours(-hoursAgo), null, Evidence: how);

    // A league with an official highlights channel on record (the ids are public; the teams stay invented).
    private static LeagueFeed WithChannel(LeagueFeed feed) =>
        feed with { League = feed.League with { Id = new LeagueId("nfl") } };

    private static SportsPage Page(LeagueFeed feed, IEnumerable<WatchEvent> sittings, SpoilerPeeks? peeks = null) =>
        SportsPage.Build([feed], [new SportsFollow(Viewer, Gulls.Id)], Viewer, sittings, Now, peeks ?? new SpoilerPeeks(), FoldMemory.Empty);

    private static TeamPlan GullsPlan(SportsPage page, FoldMemory? overlay = null) =>
        SportsScreenPlan.For(page, overlay ?? FoldMemory.Empty, Now, Zone).Single().Teams.Single();

    // ---- the builder ----

    [Fact]
    public void WatchedFinishedGamesComeNewestFirst()
    {
        var feed = Feed(LastWeek, Yesterday, ThreeDays);
        var watched = new[] { LastWeek.Id, Yesterday.Id, ThreeDays.Id };

        var lines = EarlierGames.For(feed, Gulls.Id, watched, Now);

        Assert.Equal([Yesterday.Id, ThreeDays.Id, LastWeek.Id], lines.Select(line => line.Id));
        Assert.All(lines, line => Assert.True(line.Over));
        Assert.All(lines, line => Assert.False(line.Recorded));
    }

    [Fact]
    public void OnlyWatchedOverGamesInTheWindowOfTheTeamAreListed()
    {
        var feed = Feed(LastWeek, TooOld, InProgress, Upcoming, OtherTeams, Yesterday);
        var watched = new[] { LastWeek.Id, TooOld.Id, InProgress.Id, Upcoming.Id, OtherTeams.Id };

        var lines = EarlierGames.For(feed, Gulls.Id, watched, Now);

        // Yesterday is not in the watched set; the old one is past the window; the live and future ones are not over.
        Assert.Equal([LastWeek.Id], lines.Select(line => line.Id));
    }

    [Fact]
    public void WatchedIsReadFromTheSameEvidenceAsRecordings()
    {
        var feed = Feed(LastWeek, Yesterday, ThreeDays);
        var sittings = new[] { Watch(LastWeek), Watch(Yesterday), Watch(ThreeDays), Watch(ThreeDays, WatchSource.MarkedUnwatched, hoursAgo: 1) };

        var watched = RecordedGames.Watched(feed, FollowsGulls, sittings, Now);
        var unwatched = RecordedGames.Unwatched(feed, FollowsGulls, sittings, Now);

        Assert.Equal([Yesterday.Id, LastWeek.Id], watched.OrderBy(id => id.Value));
        Assert.Equal([ThreeDays.Id], unwatched);
    }

    // ---- an open league ----

    [Fact]
    public void AnOpenLeagueCarriesTheEarlierGamesOfEachFollowedTeam()
    {
        var feed = Feed(LastWeek, Yesterday, Upcoming);
        var page = Page(feed, [Watch(LastWeek), Watch(Yesterday)]);

        var open = Assert.IsType<OpenLeague>(page.Sheets.Single());
        var earlier = Assert.Single(open.Earlier);

        Assert.Equal(Gulls.Id, earlier.Team);
        Assert.Equal([Yesterday.Id, LastWeek.Id], earlier.Games.Select(line => line.Id));
        // The watched games are not in the team's own list, which is still just the next game.
        Assert.Equal([Upcoming.Id], open.Teams.Single().Games.Select(line => line.Id));
    }

    [Fact]
    public void ThePlanFoldsTheEarlierRowsBehindAQuietLineAndOffersHighlights()
    {
        var feed = WithChannel(Feed(LastWeek, Yesterday, Upcoming));
        var team = GullsPlan(Page(feed, [Watch(LastWeek), Watch(Yesterday)]));

        Assert.False(team.EarlierOpen);
        Assert.Equal("Earlier", team.EarlierLine);
        Assert.Equal([Yesterday.Id, LastWeek.Id], team.Earlier.Select(row => row.Id));
        Assert.All(team.Earlier, row => Assert.True(row.HighlightsOffered));
        Assert.All(team.Earlier, row => Assert.False(row.Recorded));
        Assert.Equal("Gridline TV", team.Earlier[0].Channel);
        Assert.Equal([Upcoming.Id], team.Games.Select(row => row.Id));
    }

    [Fact]
    public void ALeagueWithNoHighlightsChannelStillListsTheRowsButOffersNone()
    {
        var team = GullsPlan(Page(Feed(Yesterday, Upcoming), [Watch(Yesterday)]));

        Assert.Equal([Yesterday.Id], team.Earlier.Select(row => row.Id));
        Assert.All(team.Earlier, row => Assert.False(row.HighlightsOffered));
    }

    [Fact]
    public void TheViewerOpeningTheFoldIsRemembered()
    {
        var feed = Feed(Yesterday, Upcoming);
        var page = Page(feed, [Watch(Yesterday)]);

        var opened = GullsPlan(page, FoldMemory.Empty.WithEarlier(Coastal, Gulls.Id, true));
        var closed = GullsPlan(page, FoldMemory.Empty.WithEarlier(Coastal, Gulls.Id, false));

        Assert.True(opened.EarlierOpen);
        Assert.False(closed.EarlierOpen);
    }

    [Fact]
    public void ATeamWithNothingEarlierHasNoLine()
    {
        var team = GullsPlan(Page(Feed(Upcoming), []));

        Assert.Empty(team.Earlier);
        Assert.Null(team.EarlierLine);
    }

    [Fact]
    public void TheEarlierFoldKeyIsItsOwnAndRidesTheFrozenFoldPreferences()
    {
        var memory = FoldMemory.Empty.WithTeam(Coastal, Gulls.Id, true).WithEarlier(Coastal, Gulls.Id, true);

        var stored = SportsFoldPreference.ToPreferences(memory);
        var back = SportsFoldPreference.FromPreferences(stored);

        Assert.NotEqual(FoldMemory.TeamKey(Coastal, Gulls.Id), FoldMemory.EarlierKey(Coastal, Gulls.Id));
        Assert.All(stored.Keys, key => Assert.StartsWith(SportsFoldPreference.KeyPrefix, key, StringComparison.Ordinal));
        Assert.Equal(2, stored.Count);
        Assert.True(back.Remembered(FoldMemory.EarlierKey(Coastal, Gulls.Id)));
        Assert.True(back.Remembered(FoldMemory.TeamKey(Coastal, Gulls.Id)));
    }

    // ---- the hold stays the authority (D242) ----

    [Fact]
    public void AHeldLeagueWithWatchedFinishedGamesShowsNoEarlierFold()
    {
        // One unwatched recording holds the league; two other finished games were watched.
        var feed = Feed(LastWeek, ThreeDays, Yesterday);
        var page = Page(feed, [Watch(LastWeek), Watch(ThreeDays)]);

        Assert.IsType<HeldLeague>(page.Sheets.Single());
        var team = GullsPlan(page);
        Assert.Empty(team.Earlier);
        Assert.Null(team.EarlierLine);
        Assert.False(team.EarlierOpen);
        Assert.Equal([Yesterday.Id], team.Games.Select(row => row.Id));
        Assert.All(team.Games, row => Assert.False(row.HighlightsOffered));
    }

    [Fact]
    public void HeldSheetsHaveNowhereToPutAnEarlierList()
    {
        Assert.Null(typeof(HeldLeague).GetProperty(nameof(OpenLeague.Earlier)));
        Assert.Null(typeof(TeamGames).GetProperty(nameof(OpenLeague.Earlier)));
    }

    [Fact]
    public void APeekIsUnchangedAndListsNoEarlierFold()
    {
        var feed = Feed(LastWeek, ThreeDays, Yesterday);
        var peeks = new SpoilerPeeks();
        peeks.Tap(Coastal);

        var page = Page(feed, [Watch(LastWeek), Watch(ThreeDays)], peeks);

        var open = Assert.IsType<OpenLeague>(page.Sheets.Single());
        Assert.True(open.IsPeek);
        Assert.Empty(open.Earlier);
        Assert.Equal([Yesterday.Id], open.Teams.Single().Games.Select(line => line.Id));
    }

    // ---- the card ----

    [Fact]
    public void AWatchedGameCardOffersPlayMarkUnwatchedUndoFirstAndAddTo()
    {
        var plan = ActionCardPlan.For(new CardItem("Harbor Gulls at Ridge Foxes", CardPlace.SportsGame) { GameWatched = true });

        Assert.Equal(
            [CardActionKind.Play, CardActionKind.MarkUnwatched, CardActionKind.AddTo],
            plan.Entries.Select(entry => entry.Kind));
        Assert.True(plan.Entries.Single(entry => entry.Kind == CardActionKind.MarkUnwatched).UndoFirst);
        Assert.Equal("Mark unwatched", plan.Entries[1].Label);
    }

    [Fact]
    public void AnUnwatchedGameCardIsUnchanged()
    {
        var plan = ActionCardPlan.For(new CardItem("Harbor Gulls at Ridge Foxes", CardPlace.SportsGame));

        Assert.Equal(
            [CardActionKind.Play, CardActionKind.MarkWatched, CardActionKind.AddTo],
            plan.Entries.Select(entry => entry.Kind));
    }
}
