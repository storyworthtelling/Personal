using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

public class VaultVoiceConfigStoreTests
{
    private sealed class InMemoryTextFile : IDriveTextFile
    {
        public string? Content { get; set; }
        public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Content);
        public Task WriteAsync(string content, CancellationToken cancellationToken)
        {
            Content = content;
            return Task.CompletedTask;
        }
    }

    private const string Pass = "vault-passphrase";

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_WhenNoPassphrase()
    {
        var vault = new DriveSecretsVaultStore(new InMemoryTextFile());
        await vault.SaveAsync(SecretsVault.Empty with { Claude = new ClaudeSecrets("sk-claude") }, Pass, default);

        var config = new VaultVoiceConfigStore(vault, () => null);
        Assert.True((await config.LoadAsync(default)).IsEmpty);
    }

    [Fact]
    public async Task LoadAsync_ReadsClaudeKeyFromVault()
    {
        var vault = new DriveSecretsVaultStore(new InMemoryTextFile());
        await vault.SaveAsync(SecretsVault.Empty with { Claude = new ClaudeSecrets("sk-claude") }, Pass, default);

        var config = new VaultVoiceConfigStore(vault, () => Pass);
        var loaded = await config.LoadAsync(default);
        Assert.Equal("sk-claude", loaded.ApiKey);
        Assert.Equal(VoiceConfig.Empty.Model, loaded.Model); // default tiers, as the old key-only config
    }

    [Fact]
    public async Task SaveAsync_MergesClaudeKey_WithoutClobberingOtherSections()
    {
        var store = new DriveSecretsVaultStore(new InMemoryTextFile());
        await store.SaveAsync(SecretsVault.Empty with { Plaid = new PlaidSecrets("cid", "sec") }, Pass, default);

        var config = new VaultVoiceConfigStore(store, () => Pass);
        await config.SaveAsync(new VoiceConfig("sk-new"), default);

        var result = await store.LoadAsync(Pass, default);
        Assert.Equal("sk-new", result.Vault.Claude.ApiKey); // key written
        Assert.Equal("cid", result.Vault.Plaid.ClientId);   // Ledger's section untouched
    }

    [Fact]
    public async Task SaveAsync_WithWrongPassphrase_DoesNotClobberTheVault()
    {
        var store = new DriveSecretsVaultStore(new InMemoryTextFile());
        await store.SaveAsync(SecretsVault.Empty with { Claude = new ClaudeSecrets("original") }, "real-pass", default);

        var config = new VaultVoiceConfigStore(store, () => "wrong-pass");
        await config.SaveAsync(new VoiceConfig("attempted-overwrite"), default);

        // The vault is still readable with the real passphrase and unchanged.
        var result = await store.LoadAsync("real-pass", default);
        Assert.Equal("original", result.Vault.Claude.ApiKey);
    }
}
