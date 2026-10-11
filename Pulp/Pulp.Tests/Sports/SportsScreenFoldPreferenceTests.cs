using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// What the viewer opened or folded on the Sports tab is remembered on this phone only (D241), the way
/// "Hide watched" is (D292). The pure half: which preference keys carry a fold choice and back.
/// </summary>
public class SportsScreenFoldPreferenceTests
{
    [Fact]
    public void AChoiceRoundTripsThroughThePreferenceKeys()
    {
        var memory = FoldMemory.Empty.WithLeague(Coastal, false).WithTeam(Coastal, Gulls.Id, true);

        var stored = SportsFoldPreference.ToPreferences(memory);
        var back = SportsFoldPreference.FromPreferences(stored);

        Assert.All(stored.Keys, key => Assert.StartsWith(SportsFoldPreference.KeyPrefix, key, StringComparison.Ordinal));
        Assert.Equal(memory.Choices.OrderBy(pair => pair.Key), back.Choices.OrderBy(pair => pair.Key));
        Assert.Equal(2, back.Choices.Count);
    }

    [Fact]
    public void KeysFoldCaseSoOneLeagueIsOneChoiceHoweverItsIdIsSpelled()
    {
        var upper = SportsFoldPreference.ToPreferences(FoldMemory.Empty.WithLeague(new LeagueId("CFL-Invented"), true));
        var lower = SportsFoldPreference.ToPreferences(FoldMemory.Empty.WithLeague(new LeagueId("cfl-invented"), true));

        Assert.Equal(lower.Keys, upper.Keys);
    }

    [Fact]
    public void KeysThatAreNotFoldChoicesAreIgnoredOnTheWayBack()
    {
        var stored = new Dictionary<string, bool>
        {
            [SportsFoldPreference.KeyPrefix + FoldMemory.LeagueKey(Coastal)] = true,
            ["something-else"] = false,
        };

        var back = SportsFoldPreference.FromPreferences(stored);

        Assert.True(Assert.Single(back.Choices).Value);
        Assert.True(back.Remembered(FoldMemory.LeagueKey(Coastal)));
    }

    [Fact]
    public void NothingStoredIsAnEmptyMemory()
    {
        Assert.Empty(SportsFoldPreference.FromPreferences(new Dictionary<string, bool>()).Choices);
    }
}
