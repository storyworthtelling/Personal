using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

public class LeagueFeedsAndRecordingsTests
{
    private static LeagueFeed Feed() => CoastalFeed();

    [Fact]
    public void AScheduleAndRankOrderAssembleIntoOneLeagueFeed()
    {
        var feed = Feed();
        var schedule = new LeagueSchedule(feed.League, feed.Teams, feed.Schedule, feed.Championship);

        var assembled = LeagueFeeds.Assemble(schedule, feed.Standings, feed.Highlights);

        Assert.Equal(feed.League, assembled.League);
        Assert.Equal(feed.Schedule, assembled.Schedule);
        Assert.Equal(feed.Championship, assembled.Championship);
        Assert.Equal(feed.Highlights, assembled.Highlights);
        Assert.Equal(feed.Standings.Select(team => team.Id), assembled.Standings.Select(team => team.Id));
    }

    [Fact]
    public void StandingsNamingATeamTheLeagueDoesNotHaveAreDropped()
    {
        var feed = Feed();
        var stranger = new Team(new TeamId("stranger"), "Stranger", "badge://x");
        var schedule = new LeagueSchedule(feed.League, feed.Teams, feed.Schedule, feed.Championship);

        var assembled = LeagueFeeds.Assemble(schedule, [stranger, Herons], []);

        Assert.Equal([Herons.Id], assembled.Standings.Select(team => team.Id));
    }

    private static WatchEvent Sitting(GameId game, WatchSource evidence, bool ended = false) =>
        new(new TitleId(game.Value), WatchedKind.Game, Now.AddDays(-1), null, Evidence: evidence, Ended: ended);

    [Fact]
    public void AStartedGameOfAFollowedTeamNobodyWatchedIsAnUnwatchedRecording()
    {
        var recordings = RecordedGames.Unwatched(Feed(), [Gulls.Id], [], Now);

        Assert.Equal([GullsRecording], recordings);
    }

    [Fact]
    public void AWatchedGameIsNoLongerARecording()
    {
        var recordings = RecordedGames.Unwatched(Feed(), [Gulls.Id], [Sitting(GullsRecording, WatchSource.MarkedWatched)], Now);

        Assert.Empty(recordings);
    }

    [Fact]
    public void AGameMarkedUnwatchedAgainComesBack()
    {
        var sittings = new[]
        {
            Sitting(GullsRecording, WatchSource.MarkedWatched),
            new WatchEvent(new TitleId(GullsRecording.Value), WatchedKind.Game, Now, null, Evidence: WatchSource.MarkedUnwatched),
        };

        Assert.Equal([GullsRecording], RecordedGames.Unwatched(Feed(), [Gulls.Id], sittings, Now));
    }

    [Fact]
    public void AnUpcomingGameIsNotARecordingYet()
    {
        Assert.DoesNotContain(GullsNextPlayoff, RecordedGames.Unwatched(Feed(), [Gulls.Id], [], Now));
    }

    [Fact]
    public void ARecordingOlderThanTwoWeeksIsLeftOutBecauseThePlatformHasLetItGo()
    {
        var feed = Feed();
        var old = new Game(new GameId("g-old"), Foxes, Gulls, Now.AddDays(-15), GameStage.RegularSeason, "Gridline TV");
        feed = feed with { Schedule = [.. feed.Schedule, old] };

        Assert.DoesNotContain(old.Id, RecordedGames.Unwatched(feed, [Gulls.Id], [], Now));
    }

    [Fact]
    public void ATeamNobodyFollowsHasNoRecordings()
    {
        Assert.Empty(RecordedGames.Unwatched(Feed(), [Otters.Id], [Sitting(HeronsWatched, WatchSource.MarkedWatched)], Now));
    }

    [Fact]
    public void ASittingOfAnotherKindWithTheSameIdIsNotEvidenceForTheGame()
    {
        var film = new WatchEvent(new TitleId(GullsRecording.Value), WatchedKind.Film, Now, null, Evidence: WatchSource.MarkedWatched);

        Assert.Equal([GullsRecording], RecordedGames.Unwatched(Feed(), [Gulls.Id], [film], Now));
    }
}
