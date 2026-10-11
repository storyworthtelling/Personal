using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>"Find a team to follow" (mockup 07, D67): the field narrows the leagues' teams as it is typed in.</summary>
public class SportsScreenFindTests
{
    private static readonly League CoastalL = CoastalLeague();
    private static readonly League SouthernL = new(Southern, "Southern Rules League", "badge://srl", SeasonPhase.Preseason, new DateOnly(2026, 9, 1), null);

    private static readonly IReadOnlyList<(League League, Team Team)> All =
    [
        (CoastalL, Gulls), (CoastalL, Foxes), (CoastalL, Herons), (SouthernL, new Team(new TeamId("dune-pelicans"), "Dune Pelicans", "badge://p")),
    ];

    [Fact]
    public void ATeamIsFoundByPartOfItsNameWithoutCase()
    {
        var found = SportsScreenFind.Matches(All, "  GULL ", new HashSet<TeamId>());

        var row = Assert.Single(found);
        Assert.Equal("Harbor Gulls", row.Team.Name);
        Assert.Equal("Coastal Football League", row.LeagueName);
        Assert.False(row.Following);
    }

    [Fact]
    public void ALeaguesNameBringsInItsWholeLeague()
    {
        var found = SportsScreenFind.Matches(All, "coastal", new HashSet<TeamId>());

        Assert.Equal(["Bay Herons", "Harbor Gulls", "Ridge Foxes"], found.Select(row => row.Team.Name));
    }

    [Fact]
    public void ATeamAlreadyFollowedIsMarkedAndStillListed()
    {
        var found = SportsScreenFind.Matches(All, "foxes", new HashSet<TeamId> { Foxes.Id });

        Assert.True(Assert.Single(found).Following);
    }

    [Fact]
    public void NothingTypedIsNothingFound()
    {
        Assert.Empty(SportsScreenFind.Matches(All, "   ", new HashSet<TeamId>()));
    }

    [Fact]
    public void AGroupOfMatchesIsKeptToAScreenfulAndStaysInOrder()
    {
        var many = Enumerable.Range(0, 100)
            .Select(index => (CoastalL, new Team(new TeamId($"t-{index:000}"), $"Team {index:000}", "badge://t")))
            .ToList();

        var found = SportsScreenFind.Matches(many, "team", new HashSet<TeamId>());

        Assert.Equal(SportsScreenFind.MostShown, found.Count);
        Assert.Equal("Team 000", found[0].Team.Name);
    }
}
