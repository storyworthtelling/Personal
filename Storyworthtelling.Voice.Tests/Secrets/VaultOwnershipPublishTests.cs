using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

public class VaultOwnershipPublishTests
{
    // A store that reports the vault file's Drive ownership alongside what it reads, as the Drive store does.
    private sealed class InMemoryStore : ISecretsVaultStore
    {
        public SecretsVault Vault { get; set; } = SecretsVault.Empty;
        public DriveFileOwnership Ownership { get; set; } = DriveFileOwnership.None;
        public int Saves { get; private set; }

        public Task<SecretsVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken) =>
            Task.FromResult(new SecretsVaultLoadResult(SecretsVaultStatus.Loaded, Vault, Ownership));

        public Task SaveAsync(SecretsVault vault, string passphrase, CancellationToken cancellationToken)
        {
            Vault = vault;
            Saves++;
            return Task.CompletedTask;
        }
    }

    private static SecretsVaultService Service(InMemoryStore store) =>
        new(store, () => "pass");

    // The admin's install, whose file is owned by its account, publishes as it always did.
    [Fact]
    public async Task AnOwnedFileMayPublish()
    {
        var store = new InMemoryStore { Ownership = DriveFileOwnership.Owned };

        var result = await Service(store)
            .PublishAsync(v => v with { Claude = new ClaudeSecrets("key") }, default);

        Assert.True(result.Succeeded);
        Assert.Equal("key", store.Vault.Claude.ApiKey);
    }

    // The partner's install reads the household's keys from a file shared with it and never writes
    // them. Refusing at the service makes that true everywhere at once.
    [Fact]
    public async Task ASharedFileIsReadOnlyAndNeverWritten()
    {
        var store = new InMemoryStore { Ownership = DriveFileOwnership.Shared };

        var result = await Service(store)
            .PublishAsync(v => v with { Claude = new ClaudeSecrets("overwritten") }, default);

        Assert.Equal(SecretsVaultPublishStatus.ReadOnly, result.Status);
        Assert.Equal(0, store.Saves);
    }

    // Being read-only is working as designed, so it must not nag on a settings screen.
    [Fact]
    public async Task BeingReadOnlyIsNotSomethingToActOn()
    {
        var store = new InMemoryStore { Ownership = DriveFileOwnership.Shared };

        var result = await Service(store).PublishAsync(v => v, default);

        Assert.False(result.NeedsAttention);
        Assert.False(result.Succeeded);
    }

    // The first publish creates the file, which makes this install the owner by creating it.
    [Fact]
    public async Task TheFirstPublishCreatesAnOwnedVault()
    {
        var store = new InMemoryStore { Ownership = DriveFileOwnership.None };

        var result = await Service(store)
            .PublishAsync(v => v with { Claude = new ClaudeSecrets("key") }, default);

        Assert.True(result.Succeeded);
        Assert.Equal("key", store.Vault.Claude.ApiKey);
    }

    // Publishing never stamps an account onto the vault. Ownership is the file's, not a field in it,
    // so a publish from the partner's phone cannot change who the admin is.
    [Fact]
    public async Task PublishingNeverStampsAnAccountOntoTheVault()
    {
        var store = new InMemoryStore { Ownership = DriveFileOwnership.Owned };

        await Service(store).PublishAsync(v => v, default);

        Assert.Equal(string.Empty, store.Vault.OwnerAccount);
    }
}
