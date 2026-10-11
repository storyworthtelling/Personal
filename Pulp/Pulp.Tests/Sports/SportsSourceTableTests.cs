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
        Assert.Equal(table.Leagues.Count, table.AboutLines().Count);
    }
}
