using Pulp.Domain.Sports;

namespace Pulp.Tests.Sports;

/// <summary>D208, D140: each league names the one source it is read from, and About can print the table.</summary>
public class SportsSourceTableTests
{
    private static SportsSourceTable Table() => new(
        [new SportsSourceInfo("alpha-feed", "Alpha Feed", "individual use"), new SportsSourceInfo("beta-json", "Beta JSON", "unofficial")],
        [
            new SportsLeagueEntry(new LeagueId("cfl-invented"), "Coastal Football League", "beta-json"),
            new SportsLeagueEntry(new LeagueId("srl-invented"), "Southern Rink League", "alpha-feed"),
        ]);

    [Fact]
    public void ALeagueIsReadFromTheSourceTheTableNamesForIt()
    {
        Assert.Equal("beta-json", Table().SourceOf(new LeagueId("CFL-invented")));
        Assert.Equal("alpha-feed", Table().SourceOf(new LeagueId("srl-invented")));
    }

    [Fact]
    public void ALeagueNotInTheTableHasNoSource()
    {
        Assert.Null(Table().SourceOf(new LeagueId("nowhere-invented")));
    }

    [Fact]
    public void AboutNamesEachLeaguesSourceAndItsTerms()
    {
        var lines = Table().AboutLines();

        Assert.Equal(2, lines.Count);
        Assert.Contains("Coastal Football League", lines[0]);
        Assert.Contains("Beta JSON", lines[0]);
        Assert.Contains("unofficial", lines[0]);
        Assert.Contains("Alpha Feed", lines[1]);
    }

    [Fact]
    public void ALeagueNamingAnUnknownSourceIsRefusedWhenTheTableIsBuilt()
    {
        var thrown = Assert.Throws<ArgumentException>(() => new SportsSourceTable(
            [new SportsSourceInfo("alpha-feed", "Alpha Feed", "terms")],
            [new SportsLeagueEntry(new LeagueId("cfl-invented"), "Coastal", "gamma")]));
        Assert.Contains("gamma", thrown.Message);
    }

    [Fact]
    public void ALeagueListedTwiceIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new SportsSourceTable(
            [new SportsSourceInfo("alpha-feed", "Alpha Feed", "terms")],
            [new SportsLeagueEntry(new LeagueId("cfl"), "One", "alpha-feed"), new SportsLeagueEntry(new LeagueId("CFL"), "Two", "alpha-feed")]));
    }

    [Fact]
    public void TheShippedTableCoversEveryLeagueAndNamesBothSources()
    {
        var table = SportsSourceTable.Default;

        Assert.NotEmpty(table.Leagues);
        Assert.Contains(table.Leagues, entry => entry.SourceId == SportsSourceIds.MlbStats);
        Assert.Contains(table.Leagues, entry => entry.SourceId == SportsSourceIds.Espn);
        Assert.Equal(table.All.Count, table.AboutLines().Count);
    }

    [Fact]
    public void AnUnsourcedLeagueIsInTheTableAndAboutSaysThereIsNoSourceYet()
    {
        var table = new SportsSourceTable(
            [new SportsSourceInfo("alpha-feed", "Alpha Feed", "individual use")],
            [
                new SportsLeagueEntry(new LeagueId("cfl-invented"), "Coastal", "alpha-feed"),
                new SportsLeagueEntry(new LeagueId("srl-invented"), "Southern Rink League", null, "its feed needs a key"),
            ]);

        Assert.Equal(["cfl-invented"], table.Leagues.Select(entry => entry.League.Value));
        Assert.Equal(["srl-invented"], table.Unsourced.Select(entry => entry.League.Value));
        Assert.Equal(["cfl-invented", "srl-invented"], table.All.Select(entry => entry.League.Value));
        Assert.Null(table.SourceOf(new LeagueId("srl-invented")));
        Assert.Equal("Southern Rink League — no schedule source yet: its feed needs a key", table.AboutLines()[1]);
    }

    [Fact]
    public void AnUnsourcedLeagueWithoutAReasonIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new SportsSourceTable(
            [new SportsSourceInfo("alpha-feed", "Alpha Feed", "terms")],
            [new SportsLeagueEntry(new LeagueId("srl-invented"), "Southern", null)]));
    }

    [Fact]
    public void ASourcedLeagueCarryingANoSourceNoteIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new SportsSourceTable(
            [new SportsSourceInfo("alpha-feed", "Alpha Feed", "terms")],
            [new SportsLeagueEntry(new LeagueId("cfl-invented"), "Coastal", "alpha-feed", "stray note")]));
    }
}
