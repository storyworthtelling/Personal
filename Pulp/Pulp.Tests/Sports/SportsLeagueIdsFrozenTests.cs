using Pulp.Domain.Sports;

namespace Pulp.Tests.Sports;

/// <summary>
/// The league ids are record keys: a follow is stored as a namespaced team id (<c>espn:mls:123</c>, D295), a child's
/// allowance (D317), the fold memory and the cache are keyed by the league id, so renaming one orphans what the
/// household already stored with nothing failing. The ids are frozen here, in view, and the owner's eight leagues
/// come first in the table (D340). The league is not followable until its source exists (PWHL gained one in D354), so the two without one
/// say so in a sentence rather than lying with an empty feed.
/// </summary>
public class SportsLeagueIdsFrozenTests
{
    private static readonly string[] Watched = ["mlb", "nfl", "nhl", "mls", "nwsl", "pwhl", "pwbl", "olympics"];

    [Fact]
    public void TheWatchedEightAreTheFirstEightEntriesAndTheirIdsAreFrozen()
    {
        var ids = SportsSourceTable.Default.All.Select(entry => entry.League.Value).ToList();

        Assert.Equal(Watched, ids.Take(8));
        Assert.Equal(["nba", "wnba", "afl"], ids.Skip(8));
    }

    [Fact]
    public void EveryLeagueIdIsLowercaseAndStable()
    {
        Assert.All(SportsSourceTable.Default.All, entry => Assert.Matches("^[a-z]+$", entry.League.Value));
    }

    [Fact]
    public void TheLeaguesWithASourceAreTheOnesThatHaveOneAndNoOtherIsFollowable()
    {
        var table = SportsSourceTable.Default;

        Assert.Equal(["mlb", "nfl", "nhl", "mls", "nwsl", "pwhl", "nba", "wnba", "afl"], table.Leagues.Select(entry => entry.League.Value));
        Assert.Equal(["pwbl", "olympics"], table.Unsourced.Select(entry => entry.League.Value));
        Assert.Equal("mls", table.Leagues[3].League.Value);
        Assert.Equal(SportsSourceIds.Espn, table.SourceOf(new LeagueId("mls")));
        Assert.Equal(SportsSourceIds.Espn, table.SourceOf(new LeagueId("nwsl")));
        Assert.Equal(SportsSourceIds.PwhlCalendar, table.SourceOf(new LeagueId("pwhl")));
    }

    [Fact]
    public void AnUnsourcedLeagueHasNoSourceAndSaysWhyInItsOwnWords()
    {
        foreach (var entry in SportsSourceTable.Default.Unsourced)
        {
            Assert.False(entry.IsSourced);
            Assert.Null(SportsSourceTable.Default.SourceOf(entry.League));
            Assert.False(string.IsNullOrWhiteSpace(entry.NoSourceNote));
        }
    }

    [Fact]
    public void TheNewLeaguesHaveATypicalGameLengthAndTheUnknownFallbackStaysTheLongest()
    {
        Assert.Equal(TimeSpan.FromMinutes(150), GameOver.LengthOf(new LeagueId("mls")));
        Assert.Equal(TimeSpan.FromMinutes(150), GameOver.LengthOf(new LeagueId("nwsl")));
        Assert.Equal(TimeSpan.FromHours(3), GameOver.LengthOf(new LeagueId("pwhl")));
        Assert.Equal(TimeSpan.FromHours(3), GameOver.LengthOf(new LeagueId("pwbl")));
        Assert.Equal(TimeSpan.FromHours(3), GameOver.LengthOf(new LeagueId("wnba")));
        Assert.Equal(TimeSpan.FromHours(4), GameOver.LengthOf(new LeagueId("nowhere-invented")));
    }

    [Fact]
    public void OnlyTheLeaguesWithAVerifiedOfficialChannelAreOfferedHighlights()
    {
        foreach (var verified in new[] { "nfl", "mlb", "nba", "nhl", "mls", "nwsl" })
        {
            Assert.True(HighlightChannels.Default.Has(new LeagueId(verified)), verified);
        }

        foreach (var without in new[] { "pwhl", "pwbl", "olympics", "wnba", "afl" })
        {
            Assert.False(HighlightChannels.Default.Has(new LeagueId(without)), without);
        }
    }

    [Fact]
    public void ThePlayoffAndChampionshipStagesRevealSoTheHoldTreatsThemSo()
    {
        Assert.True(SpoilerHold.Reveals(GameStage.Playoffs));
        Assert.True(SpoilerHold.Reveals(GameStage.Championship));
        Assert.False(SpoilerHold.Reveals(GameStage.RegularSeason));
    }
}
