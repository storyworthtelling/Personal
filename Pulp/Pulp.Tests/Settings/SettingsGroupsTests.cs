using Pulp.Domain.Settings;

namespace Pulp.Tests.Settings;

/// <summary>Mockup 09's groups and their header lines (D162, D250, D140, D125).</summary>
public class SettingsGroupsTests
{
    /// <summary>D162 less TV, which waits for its own phase (D187); no Sports group (teams live on the Sports tab).</summary>
    [Fact]
    public void TheGroupsAreInTheDrawnOrder() =>
        Assert.Equal(["Services", "Household", "Discover", "Vault", "About", "Diagnostics"], SettingsGroups.Order);

    [Fact]
    public void TheHouseholdLineNamesEveryone() =>
        Assert.Equal("Sam, Alex, Robin", SettingsGroups.HouseholdMeta(["Sam", "Alex", "Robin"]));

    [Fact]
    public void AnEmptyHouseholdSaysSo() => Assert.Equal("Nobody yet", SettingsGroups.HouseholdMeta([]));

    [Fact]
    public void TheVaultIsWorkingOnlyWhenNoKeyWantsEntering()
    {
        Assert.Equal("Working", SettingsGroups.VaultMeta([false, false, false]));
        Assert.Equal("1 key needed", SettingsGroups.VaultMeta([false, true, false]));
        Assert.Equal("3 keys needed", SettingsGroups.VaultMeta([true, true, true]));
    }

    [Fact]
    public void TheVaultWithNoKeysToAskAboutIsAProgrammingError() =>
        Assert.Throws<ArgumentException>(() => SettingsGroups.VaultMeta([]));

    [Theory]
    [InlineData("1.0.4", "Pulp 1.0.4")]
    [InlineData(null, "Pulp")]
    [InlineData(" ", "Pulp")]
    public void AboutNamesTheVersion(string? version, string expected) =>
        Assert.Equal(expected, SettingsGroups.AboutMeta(version));

    [Fact]
    public void DiagnosticsSaysWhatItDoes() => Assert.Equal("Export the log", SettingsGroups.DiagnosticsMeta);

    /// <summary>D140: each source whose terms ask for a credit is named in About.</summary>
    [Theory]
    [InlineData("Streaming Availability")]
    [InlineData("TVmaze")]
    [InlineData("OMDb")]
    [InlineData("YouTube")]
    public void AboutCreditsEverySourceThatAsks(string source)
    {
        Assert.NotEmpty(SettingsGroups.Credits);
        Assert.Contains(SettingsGroups.Credits, line => line.Contains(source, StringComparison.Ordinal));
    }

    /// <summary>TVmaze's data is CC BY-SA: the credit has to say so, not just name the site.</summary>
    [Fact]
    public void TheTvMazeCreditCarriesItsLicence() =>
        Assert.Contains(SettingsGroups.Credits, line => line.Contains("TVmaze", StringComparison.Ordinal) && line.Contains("CC BY-SA", StringComparison.Ordinal));
}
