using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// Audit 11 §13, second half: a refused shared key seen by one app never reached the others. Folio's Claude field read
/// only Folio's own health, so a 401 in Post or Plate left it saying "Working ✓". The vault now carries the verdict on
/// each shared credential, stamped with a fingerprint of the vault's value, so a verdict is only ever applied to the key
/// it was about.
/// </summary>
public class VaultKeyHealthTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 11, 13, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 11, 13, 7, 0, 0, TimeSpan.Zero);

    private static SecretsVault Vault(string claude = "example-claude-key") =>
        SecretsVault.Empty with { Claude = new ClaudeSecrets(claude) };

    private static string? Same(string id) => null;

    [Fact]
    public void ARefusalSeenHereIsCarriedToTheVault()
    {
        var local = CredentialHealthReport.Empty.MarkFailing(SharedCredentialIds.Claude, "invalid x-api-key", Later);

        var pushed = VaultKeyHealth.Push(Vault(), local, Same);

        Assert.NotNull(pushed);
        var entry = pushed!.KeyHealth[SharedCredentialIds.Claude];
        Assert.Equal(CredentialStatus.Failing, entry.Status);
        Assert.Equal(VaultKeyHealth.Fingerprint("example-claude-key"), entry.Fingerprint);
    }

    [Fact]
    public void TheFingerprintIsNotTheKey()
    {
        var fingerprint = VaultKeyHealth.Fingerprint("example-claude-key");

        Assert.DoesNotContain("example", fingerprint);
        Assert.Equal(16, fingerprint.Length);
    }

    [Fact]
    public void AnotherAppPicksTheRefusalUp()
    {
        var vault = VaultKeyHealth.Push(Vault(), CredentialHealthReport.Empty.MarkFailing(SharedCredentialIds.Claude, "401", Later), Same)!;
        var folio = CredentialHealthReport.Empty.MarkOk(SharedCredentialIds.Claude, Earlier);

        var pulled = VaultKeyHealth.Pull(vault, folio, _ => "example-claude-key");

        Assert.Equal(CredentialStatus.Failing, pulled.Get(SharedCredentialIds.Claude).Status);
    }

    [Fact]
    public void AnOlderVerdictDoesNotOverrideANewerOneHere()
    {
        var vault = VaultKeyHealth.Push(Vault(), CredentialHealthReport.Empty.MarkFailing(SharedCredentialIds.Claude, "401", Earlier), Same)!;
        var here = CredentialHealthReport.Empty.MarkOk(SharedCredentialIds.Claude, Later);

        Assert.Equal(CredentialStatus.Ok, VaultKeyHealth.Pull(vault, here, Same).Get(SharedCredentialIds.Claude).Status);
    }

    [Fact]
    public void ARotatedKeyDoesNotInheritTheOldKeysRefusal()
    {
        var refused = VaultKeyHealth.Push(Vault("example-old-key"),
            CredentialHealthReport.Empty.MarkFailing(SharedCredentialIds.Claude, "401", Later), Same)!;
        var rotated = refused with { Claude = new ClaudeSecrets("example-new-key") };

        var pulled = VaultKeyHealth.Pull(rotated, CredentialHealthReport.Empty, Same);

        Assert.Equal(CredentialStatus.Unknown, pulled.Get(SharedCredentialIds.Claude).Status);
    }

    [Fact]
    public void AnAppHoldingADifferentValueNeitherPushesNorPulls()
    {
        var local = CredentialHealthReport.Empty.MarkFailing(SharedCredentialIds.Claude, "401", Later);

        Assert.Null(VaultKeyHealth.Push(Vault(), local, _ => "example-stale-local-key"));

        var vault = VaultKeyHealth.Push(Vault(), local, Same)!;
        Assert.Equal(CredentialStatus.Unknown,
            VaultKeyHealth.Pull(vault, CredentialHealthReport.Empty, _ => "example-stale-local-key").Get(SharedCredentialIds.Claude).Status);
    }

    [Fact]
    public void NothingNewMeansNoWrite()
    {
        var local = CredentialHealthReport.Empty.MarkFailing(SharedCredentialIds.Claude, "401", Later);
        var vault = VaultKeyHealth.Push(Vault(), local, Same)!;

        Assert.Null(VaultKeyHealth.Push(vault, local, Same));
        Assert.Null(VaultKeyHealth.Push(Vault(), CredentialHealthReport.Empty, Same));
    }

    [Fact]
    public void OnlyTheSharedCredentialsTravel()
    {
        var local = CredentialHealthReport.Empty.MarkFailing("plaid", "401", Later);

        Assert.Null(VaultKeyHealth.Push(Vault() with { }, local, Same));
        Assert.Equal([SharedCredentialIds.Claude, SharedCredentialIds.Gemini, SharedCredentialIds.Remarkable], VaultKeyHealth.Ids);
    }

    [Fact]
    public void TheVerdictsRoundTripThroughTheVaultFile()
    {
        var vault = VaultKeyHealth.Push(Vault(), CredentialHealthReport.Empty.MarkFailing(SharedCredentialIds.Claude, "401", Later), Same)!;

        var read = System.Text.Json.JsonSerializer.Deserialize<SecretsVault>(System.Text.Json.JsonSerializer.Serialize(vault))!;

        Assert.Equal(vault.KeyHealth[SharedCredentialIds.Claude], read.KeyHealth[SharedCredentialIds.Claude]);
    }
}
