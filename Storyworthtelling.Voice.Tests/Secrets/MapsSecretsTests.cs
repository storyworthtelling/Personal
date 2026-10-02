using System.Text.Json;
using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// The Google Maps Platform section: one key, and the house it measures from.
///
/// <para>One key rather than four, because Maps Platform bills a project rather than a product —
/// routes, weather, pollen and air quality are all the same credential with different APIs enabled
/// on it. Splitting it into four vault fields would invite four places to paste the same string and
/// three of them to be stale.</para>
/// </summary>
public class MapsSecretsTests
{
    [Fact]
    public void AnEmptyVaultCarriesTheMapsSection()
    {
        Assert.NotNull(SecretsVault.Empty.Maps);
        Assert.Equal(string.Empty, SecretsVault.Empty.Maps.ApiKey);
    }

    [Fact]
    public void TheMapsSectionSurvivesAJsonRoundTrip()
    {
        var vault = SecretsVault.Empty with
        {
            Maps = new MapsSecrets("key-abc", "100 Main St, Springfield IL", 39.7817, -89.6501),
        };

        var restored = JsonSerializer.Deserialize<SecretsVault>(JsonSerializer.Serialize(vault))!;

        Assert.Equal("key-abc", restored.Maps.ApiKey);
        Assert.Equal("100 Main St, Springfield IL", restored.Maps.HomeAddress);
        Assert.Equal(39.7817, restored.Maps.HomeLatitude);
        Assert.Equal(-89.6501, restored.Maps.HomeLongitude);
    }

    /// <summary>
    /// This is what an in-place upgrade actually does: every vault already in the owner's Drive was
    /// written before this section existed, and must read with it empty rather than missing.
    /// </summary>
    [Fact]
    public void AVaultWrittenBeforeMapsExistedStillReads()
    {
        const string older = """{"Claude":{"ApiKey":"k"},"Music":{"FolderId":"f"}}""";

        var restored = JsonSerializer.Deserialize<SecretsVault>(older)!;

        Assert.Equal("k", restored.Claude.ApiKey);
        Assert.NotNull(restored.Maps);
        Assert.Equal(string.Empty, restored.Maps.ApiKey);
    }

    [Fact]
    public void PublishingMapsLeavesEveryOtherSectionAlone()
    {
        var vault = SecretsVault.Empty with { Claude = new ClaudeSecrets("claude-key") };

        var merged = vault with { Maps = new MapsSecrets("key-abc") };

        Assert.Equal("claude-key", merged.Claude.ApiKey);
        Assert.Equal("key-abc", merged.Maps.ApiKey);
    }
}
