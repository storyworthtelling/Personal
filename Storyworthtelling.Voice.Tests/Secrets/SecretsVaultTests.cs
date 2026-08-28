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
        Assert.Equal(string.Empty, vault.Tidal.ClientId);
    }

    [Fact]
    public void TidalSectionSurvivesAJsonRoundTrip()
    {
        var vault = SecretsVault.Empty with { Tidal = new TidalSecrets("client-abc", "secret-def") };

        var restored = JsonSerializer.Deserialize<SecretsVault>(JsonSerializer.Serialize(vault))!;

        Assert.Equal("client-abc", restored.Tidal.ClientId);
        Assert.Equal("secret-def", restored.Tidal.ClientSecret);
    }

    // The line the split turns on: the shared vault holds the app, never a listener.
    // A refresh token that found its way back in here would put both installs on one
    // membership — the single-stream problem the Family plan was bought to solve — and
    // would make either person's sign-out silently sign the other out too.
    [Fact]
    public void TheSharedVaultHoldsNoPersonalSignIn()
    {
        var written = JsonSerializer.Serialize(
            SecretsVault.Empty with { Tidal = new TidalSecrets("client-abc", "secret-def") });

        Assert.DoesNotContain("RefreshToken", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UserId", written, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishingTidalLeavesEveryOtherSectionAlone()
    {
        var vault = SecretsVault.Empty with
        {
            Claude = new ClaudeSecrets("claude-key"),
            Music = new MusicSecrets("folder-1"),
        };

        var merged = vault with { Tidal = new TidalSecrets("client-abc", "secret-def") };

        Assert.Equal("claude-key", merged.Claude.ApiKey);
        Assert.Equal("folder-1", merged.Music.FolderId);
        Assert.Equal("client-abc", merged.Tidal.ClientId);
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
        Assert.Equal(string.Empty, restored.Tidal.ClientId);
    }

    // An older vault that DID carry a refresh token — this session wrote that shape
    // before the Family plan changed the design — still reads. The stale field is
    // ignored rather than fought over; the token is re-obtained by signing in, which
    // is the one thing that has to happen per person anyway.
    [Fact]
    public void AVaultWrittenWhileTidalWasSharedStillReads()
    {
        const string older = """{"Tidal":{"ClientId":"c","ClientSecret":"s","RefreshToken":"stale"}}""";

        var restored = JsonSerializer.Deserialize<SecretsVault>(older)!;

        Assert.Equal("c", restored.Tidal.ClientId);
        Assert.Equal("s", restored.Tidal.ClientSecret);
    }
}

public class PersonalVaultTests
{
    [Fact]
    public void EmptyPersonalVaultCarriesEverySection()
    {
        var vault = PersonalVault.Empty;

        Assert.NotNull(vault.TidalAccount);
        Assert.Equal(string.Empty, vault.TidalAccount.RefreshToken);
    }

    [Fact]
    public void TidalAccountSurvivesAJsonRoundTrip()
    {
        var vault = PersonalVault.Empty with { TidalAccount = new TidalAccount("user-1", "refresh-ghi") };

        var restored = JsonSerializer.Deserialize<PersonalVault>(JsonSerializer.Serialize(vault))!;

        Assert.Equal("user-1", restored.TidalAccount.UserId);
        Assert.Equal("refresh-ghi", restored.TidalAccount.RefreshToken);
    }

    // Same tolerance as the shared vault, for the same reason: a personal vault
    // written by an older build must not take the app down on the next launch.
    [Fact]
    public void APersonalVaultWrittenBeforeTidalExistedStillReads()
    {
        var restored = JsonSerializer.Deserialize<PersonalVault>("{}")!;

        Assert.NotNull(restored.TidalAccount);
        Assert.Equal(string.Empty, restored.TidalAccount.UserId);
    }
}
