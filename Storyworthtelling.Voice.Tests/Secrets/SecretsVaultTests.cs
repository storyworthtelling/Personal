using System.Text.Json;
using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Tests.Secrets;

public class SecretsVaultTests
{
    // Every section defaults to empty so a vault only one app has ever written still
    // round-trips for the others. A new section that forgets this deserializes to
    // null and throws in whichever app reads it next.
    [Fact]
    public void EmptyVaultCarriesEverySection()
    {
        var vault = SecretsVault.Empty;

        Assert.NotNull(vault.Claude);
        Assert.NotNull(vault.Tidal);
        Assert.Equal(string.Empty, vault.Tidal.RefreshToken);
    }

    [Fact]
    public void TidalSectionSurvivesAJsonRoundTrip()
    {
        var vault = SecretsVault.Empty with
        {
            Tidal = new TidalSecrets("client-abc", "secret-def", "refresh-ghi"),
        };

        var restored = JsonSerializer.Deserialize<SecretsVault>(JsonSerializer.Serialize(vault))!;

        Assert.Equal("client-abc", restored.Tidal.ClientId);
        Assert.Equal("secret-def", restored.Tidal.ClientSecret);
        Assert.Equal("refresh-ghi", restored.Tidal.RefreshToken);
    }

    // The reason Tidal is one shared section rather than one per person: the account
    // is shared, so both installs restore the same token from the same vault.
    [Fact]
    public void PublishingTidalLeavesEveryOtherSectionAlone()
    {
        var vault = SecretsVault.Empty with
        {
            Claude = new ClaudeSecrets("claude-key"),
            Music = new MusicSecrets("folder-1"),
        };

        var merged = vault with { Tidal = new TidalSecrets(RefreshToken: "refresh-ghi") };

        Assert.Equal("claude-key", merged.Claude.ApiKey);
        Assert.Equal("folder-1", merged.Music.FolderId);
        Assert.Equal("refresh-ghi", merged.Tidal.RefreshToken);
    }

    // A vault written before Tidal existed must still read, with the new section
    // empty rather than missing — this is what an in-place upgrade actually does.
    [Fact]
    public void AVaultWrittenBeforeTidalExistedStillReads()
    {
        const string older = """{"Claude":{"ApiKey":"k"},"Music":{"FolderId":"f"}}""";

        var restored = JsonSerializer.Deserialize<SecretsVault>(older)!;

        Assert.Equal("k", restored.Claude.ApiKey);
        Assert.NotNull(restored.Tidal);
        Assert.Equal(string.Empty, restored.Tidal.RefreshToken);
    }
}
