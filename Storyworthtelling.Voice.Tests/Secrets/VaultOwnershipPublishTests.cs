using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

public class VaultOwnershipPublishTests
{
    private sealed class InMemoryStore : ISecretsVaultStore
    {
        public SecretsVault Vault { get; set; } = SecretsVault.Empty;
        public int Saves { get; private set; }

        public Task<SecretsVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken) =>
            Task.FromResult(SecretsVaultLoadResult.Loaded(Vault));

        public Task SaveAsync(SecretsVault vault, string passphrase, CancellationToken cancellationToken)
        {
            Vault = vault;
            Saves++;
            return Task.CompletedTask;
        }
    }

    private static SecretsVaultService Service(InMemoryStore store, string? account) =>
        new(store, () => "pass", () => account);

    // The owner's install publishes as it always did.
    [Fact]
    public async Task TheOwnerPublishes()
    {
        var store = new InMemoryStore { Vault = SecretsVault.Empty with { OwnerAccount = "owner@example.com" } };

        var result = await Service(store, "owner@example.com")
            .PublishAsync(v => v with { Claude = new ClaudeSecrets("key") }, default);

        Assert.True(result.Succeeded);
        Assert.Equal("key", store.Vault.Claude.ApiKey);
    }

    // Her install reads the household's keys and never writes them. Refusing at the
    // service is what makes that true everywhere at once, rather than asking each of
    // the nine publish call sites across the suite to remember.
    [Fact]
    public async Task AnotherPersonsInstallDoesNotWrite()
    {
        var store = new InMemoryStore { Vault = SecretsVault.Empty with { OwnerAccount = "owner@example.com" } };

        var result = await Service(store, "her@example.com")
            .PublishAsync(v => v with { Claude = new ClaudeSecrets("overwritten") }, default);

        Assert.Equal(SecretsVaultPublishStatus.ReadOnly, result.Status);
        Assert.Equal(0, store.Saves);
    }

    // Not an error: her install is working exactly as designed, so it must not nag on
    // a settings screen the way an unreadable vault does.
    [Fact]
    public async Task BeingReadOnlyIsNotSomethingToActOn()
    {
        var store = new InMemoryStore { Vault = SecretsVault.Empty with { OwnerAccount = "owner@example.com" } };

        var result = await Service(store, "her@example.com").PublishAsync(v => v, default);

        Assert.False(result.NeedsAttention);
        Assert.False(result.Succeeded);
    }

    // The first publish claims the vault, so ownership is taken by using it.
    [Fact]
    public async Task TheFirstPublishClaimsTheVault()
    {
        var store = new InMemoryStore();

        var result = await Service(store, "first@example.com")
            .PublishAsync(v => v with { Claude = new ClaudeSecrets("key") }, default);

        Assert.True(result.Succeeded);
        Assert.Equal("first@example.com", store.Vault.OwnerAccount);
        Assert.Equal("key", store.Vault.Claude.ApiKey);
    }

    // Publishing never re-stamps an owned vault, whoever is writing.
    [Fact]
    public async Task PublishingNeverMovesOwnership()
    {
        var store = new InMemoryStore { Vault = SecretsVault.Empty with { OwnerAccount = "owner@example.com" } };

        await Service(store, "owner@example.com").PublishAsync(v => v, default);

        Assert.Equal("owner@example.com", store.Vault.OwnerAccount);
    }

    // An app that has no Google account bound yet — every head before sign-in — must
    // keep behaving as it did before ownership existed, or the vault stops working on
    // the owner's own phone during setup.
    [Fact]
    public async Task AnInstallWithNoAccountStillPublishes()
    {
        var store = new InMemoryStore();

        var result = await Service(store, null).PublishAsync(v => v with { Claude = new ClaudeSecrets("key") }, default);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, store.Vault.OwnerAccount);
    }
}
