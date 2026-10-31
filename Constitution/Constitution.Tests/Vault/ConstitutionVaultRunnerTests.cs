using Constitution.Infrastructure.Vault;

namespace Constitution.Tests.Vault;

/// <summary>
/// The orchestration the Settings screen and first run lean on, run through its seams (S1, review of
/// Release A). A phone that has just been factory-reset takes its steps in the order launch (no
/// passphrase), sign-in, passphrase typed last; the typed passphrase has to bring the vault back, not
/// merely start publishing.
/// </summary>
public class ConstitutionVaultRunnerTests
{
    private static ConstitutionVaultRunner RunnerFor(
        TestPhone phone, Func<string?>? passphrase = null, Func<bool>? signedIn = null, Func<ConstitutionVaultSync>? factory = null) =>
        new(passphrase ?? (() => Invented.Passphrase), signedIn ?? (() => true), factory ?? (() => phone.Sync), phone.Log);

    [Fact]
    public async Task ATypedPassphraseRestoresTheVaultBeforeAnythingIsPublished()
    {
        // Reset phone: nothing local. The vaults hold the household's connections.
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);

        var result = await RunnerFor(phone).RestoreThenPublishAsync([], default);

        // The restore ran, and filled what the reset wiped.
        Assert.NotNull(result.Restore);
        Assert.Equal(Invented.SleepLogin, phone.Local.SleepNumberLogin);
        Assert.Equal(Invented.SleepWord, phone.Local.SleepNumberPassword);
        Assert.Equal(Invented.RemarkableToken, phone.Local.RemarkableDeviceToken);
        Assert.Equal(Invented.ClaudeKey, phone.Local.ClaudeApiKey);
        Assert.Equal(Invented.HealthChoices, phone.Local.HealthConnectChoicesJson);

        // And then the publish ran, and found nothing of this install's to add.
        Assert.Equal(VaultRunStatus.Published, result.Status);
        Assert.NotNull(result.Publish);
        Assert.Equal(
            Invented.WithoutTheSectionsConstitutionWrites(cloud.ReadShared(Invented.Passphrase)),
            Invented.WithoutTheSectionsConstitutionWrites(Invented.Shared()));
        Assert.Equal(Invented.SleepLogin, cloud.ReadShared(Invented.Passphrase).Constitution.SleepNumberLogin);
    }

    [Fact]
    public async Task TheRestoreIsRecordedBeforeThePublish()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);

        await RunnerFor(phone).RestoreThenPublishAsync([], default);

        var restore = phone.Log.Lines.FindIndex(line => line.StartsWith("vault restore", StringComparison.Ordinal));
        var publish = phone.Log.Lines.FindIndex(line => line.StartsWith("vault publish", StringComparison.Ordinal));
        Assert.True(restore >= 0, "No restore line was recorded.");
        Assert.True(publish > restore, "The publish was recorded before, or without, the restore.");
    }

    [Fact]
    public async Task WithoutAPassphraseNothingIsReadOrWritten()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        var runner = RunnerFor(phone, passphrase: () => null);

        Assert.Equal(VaultRunStatus.NoPassphrase, (await runner.RestoreAsync(default)).Status);
        Assert.Equal(VaultRunStatus.NoPassphrase, (await runner.PublishAsync([], default)).Status);
        Assert.Equal(VaultRunStatus.NoPassphrase, (await runner.RestoreThenPublishAsync([], default)).Status);
        Assert.Equal(0, cloud.SharedDrive.Reads + cloud.SharedDrive.Writes);
    }

    [Fact]
    public async Task WithoutASignInNothingIsReadOrWritten()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        var runner = RunnerFor(phone, signedIn: () => false);

        Assert.Equal(VaultRunStatus.NotSignedIn, (await runner.RestoreAsync(default)).Status);
        Assert.Equal(VaultRunStatus.NotSignedIn, (await runner.PublishAsync([], default)).Status);
        Assert.Equal(VaultRunStatus.NotSignedIn, (await runner.RestoreThenPublishAsync([], default)).Status);
        Assert.Equal(0, cloud.SharedDrive.Reads + cloud.SharedDrive.Writes);
    }

    [Fact]
    public async Task TheTypedPassphraseIsReadWhenTheCallIsMadeNotWhenTheRunnerWasBuilt()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        string? typed = null;
        var runner = RunnerFor(phone, passphrase: () => typed);

        Assert.Equal(VaultRunStatus.NoPassphrase, (await runner.RestoreThenPublishAsync([], default)).Status);

        typed = Invented.Passphrase;
        Assert.Equal(VaultRunStatus.Published, (await runner.RestoreThenPublishAsync([], default)).Status);
        Assert.Equal(Invented.SleepLogin, phone.Local.SleepNumberLogin);
    }

    [Fact]
    public async Task AFailedRestoreIsAResultAndStopsBeforeAnyPublish()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        var builds = 0;
        var runner = RunnerFor(phone, factory: () =>
        {
            builds++;
            throw new InvalidOperationException("echoes " + Invented.Passphrase);
        });

        var result = await runner.RestoreThenPublishAsync([VaultField.SleepNumberLogin], default);

        Assert.Equal(VaultRunStatus.Failed, result.Status);
        Assert.Equal(nameof(InvalidOperationException), result.Detail);
        Assert.Equal(1, builds);

        // The type name is all that is recorded; the message is not ours to vouch for.
        Assert.All(phone.Log.Lines, line => Assert.DoesNotContain(Invented.Passphrase, line, StringComparison.Ordinal));
        Assert.Contains(phone.Log.Lines, line => line.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
    }

    [Fact]
    public async Task APublishCarriesWhatTheCallerSaidChanged()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.FillLocalSharedConnections();
        phone.Local.SleepNumberLogin = "newer@example.com";
        phone.Local.RemarkableDeviceToken = "example-newer-token";

        await RunnerFor(phone).PublishAsync([VaultField.SleepNumberLogin], default);

        var after = cloud.ReadShared(Invented.Passphrase);
        Assert.Equal("newer@example.com", after.Constitution.SleepNumberLogin);
        Assert.Equal(Invented.RemarkableToken, after.Remarkable.DeviceToken);
    }
}
