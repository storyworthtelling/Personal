using Pulp.Domain.Services;
using Pulp.Domain.Settings;
using Pulp.Domain.Sports;

namespace Pulp.Tests.Settings;

/// <summary>D140, D310: About prints the league to source table and the broadcaster to service table as they are.</summary>
public class SportsTablesAboutTests
{
    [Fact]
    public void AboutPrintsEveryLeagueSourceLineAndEveryBroadcasterLine()
    {
        var printed = SettingsGroups.SportsTables();

        Assert.All(SportsSourceTable.Default.AboutLines(), line => Assert.Contains(line, printed));
        Assert.All(BroadcasterTable.Public.AboutLines(), line => Assert.Contains(line, printed));
    }

    [Fact]
    public void TheTwoTablesAreSeparatedByASentenceSayingWhatTheSecondIs()
    {
        var printed = SettingsGroups.SportsTables();
        var firstBroadcaster = printed.ToList().IndexOf(BroadcasterTable.Public.AboutLines()[0]);

        Assert.True(firstBroadcaster > SportsSourceTable.Default.AboutLines().Count, "There is no sentence between the tables.");
    }

    [Fact]
    public void AboutSaysPlainlyWhichWatchedLeaguesHaveNoScheduleSourceYet()
    {
        var printed = SettingsGroups.SportsTables();

        foreach (var name in new[] { "PWBL", "Olympics" })
        {
            Assert.Contains(printed, line => line.StartsWith(name + " — no schedule source yet", StringComparison.Ordinal));
        }

        Assert.Contains(printed, line => line.StartsWith("PWHL — read from the PWHL's own calendar file", StringComparison.Ordinal));
        Assert.DoesNotContain(printed, line => line.StartsWith("PWHL — no schedule source yet", StringComparison.Ordinal));
        Assert.Contains(printed, line => line.StartsWith("MLS — read from ESPN", StringComparison.Ordinal));
        Assert.Contains(printed, line => line.StartsWith("NWSL — read from ESPN", StringComparison.Ordinal));
    }
}
