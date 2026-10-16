using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// D313, D314: highlights are feed text and can spoil, so they are offered only where nothing is held, only for
/// a game that is over, only for a league with an official channel on record, and drawn as a generic label
/// that never carries the video's own title.
/// </summary>
public class HighlightsOfferTests
{
    private static readonly LeagueId WithChannel = new("nfl");

    private static GameLine Over() =>
        new(new GameId("g-over"), Foxes, Gulls, Now.AddDays(-3), GameStage.RegularSeason, "Gridline TV", Recorded: false);

    private static OpenLeague Open(LeagueId id, bool peek, params GameLine[] games) =>
        new(new LeagueLine(id, "Invented League", "badge://x", SeasonPhase.RegularSeason, null),
            [new TeamGames(Gulls, games)], null, [], [new Highlight("vid-spoiler", "Gulls crush Foxes 42-3")], peek);

    [Fact]
    public void AnOverGameInAnOpenLeagueWithAChannelIsOffered()
    {
        Assert.True(HighlightsOffer.For(Open(WithChannel, peek: false, Over()), Over(), Now));
    }

    [Fact]
    public void AHeldLeaguesPeekIsNeverOffered()
    {
        Assert.False(HighlightsOffer.For(Open(WithChannel, peek: true, Over()), Over(), Now));
    }

    [Fact]
    public void ALeagueWithNoOfficialChannelOnRecordIsNeverOffered()
    {
        Assert.False(HighlightsOffer.For(Open(Coastal, peek: false, Over()), Over(), Now));
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(60)]
    [InlineData(-3 * 60)]
    public void AGameNotYetOverIsNotOffered(int minutesFromNow)
    {
        var game = Over() with { StartsAt = Now.AddMinutes(minutesFromNow) };

        Assert.False(HighlightsOffer.For(Open(WithChannel, peek: false, game), game, Now));
    }

    [Fact]
    public void ACompletedGameIsOverAfterTheAllowanceForItsLength()
    {
        var game = Over() with { StartsAt = Now - HighlightsOffer.GameLength - TimeSpan.FromMinutes(1) };

        Assert.True(HighlightsOffer.For(Open(WithChannel, peek: false, game), game, Now));
    }

    [Fact]
    public void TheLabelIsGenericWithTheTeamsAndTheDate()
    {
        var label = HighlightsOffer.Label("Foxes at Gulls", Over().StartsAt, TimeZoneInfo.Utc);

        Assert.Equal("Highlights · Foxes at Gulls · Sat Jan 17", label);
    }

    [Fact]
    public void ARowBuiltFromAnOfferedGameCarriesTheLabelAndNothingFromTheVideo()
    {
        var row = GameRow.From(Over(), Now, TimeZoneInfo.Utc, highlights: true);

        Assert.True(row.HighlightsOffered);
        Assert.DoesNotContain("crush", row.HighlightsLabel, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Highlights", row.HighlightsLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowNotOfferedHasNoLabel()
    {
        var row = GameRow.From(Over(), Now, TimeZoneInfo.Utc);

        Assert.False(row.HighlightsOffered);
        Assert.Null(row.HighlightsLabel);
    }

    [Fact]
    public void ThePlanOffersHighlightsOnlyOnOpenLeaguesRows()
    {
        var page = new SportsPage([Open(WithChannel, peek: false, Over()), Open(new LeagueId("nba"), peek: true, Over())],
            LeagueFold.Starting([], FoldMemory.Empty));
        var plans = SportsScreenPlan.For(page, FoldMemory.Empty, Now, TimeZoneInfo.Utc);

        Assert.True(plans[0].Teams.SelectMany(team => team.Games).All(game => game.HighlightsOffered));
        Assert.True(plans[1].Teams.SelectMany(team => team.Games).All(game => !game.HighlightsOffered));
    }

    [Fact]
    public void TheScheduleAndBracketPagesFollowTheSameRule()
    {
        var open = Open(WithChannel, peek: false, Over());
        var peek = Open(WithChannel, peek: true, Over());

        Assert.All(SportsScreenPages.Schedule(open, Now, TimeZoneInfo.Utc), row => Assert.True(row.HighlightsOffered));
        Assert.All(SportsScreenPages.Schedule(peek, Now, TimeZoneInfo.Utc), row => Assert.False(row.HighlightsOffered));
    }

    [Fact]
    public void AHeldLeaguesRowsNeverCarryTheNextRevealingFixtureOrItsChannel()
    {
        var held = SpoilerHold.Present(CoastalFeed(), [Gulls.Id], [GullsRecording], Now, peeking: false);
        var page = new SportsPage([held], LeagueFold.Starting([], FoldMemory.Empty));
        var rows = SportsScreenPlan.For(page, FoldMemory.Empty, Now, TimeZoneInfo.Utc).SelectMany(league => league.Teams).SelectMany(team => team.Games).ToList();

        Assert.DoesNotContain(rows, row => row.Id == GullsNextPlayoff || row.Id == HeronsNextPlayoff);
        Assert.All(rows, row => Assert.False(row.HighlightsOffered));
    }
}
