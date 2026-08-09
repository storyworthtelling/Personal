using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

public class DriveSecretsVaultStoreTests
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
    public async Task LoadAsync_ReturnsEmpty_WhenNoFileYet()
    {
        var store = new DriveSecretsVaultStore(new InMemoryTextFile { Content = null });
        Assert.Equal(SecretsVaultStatus.Empty, (await store.LoadAsync(Pass, default)).Status);

        var blank = new DriveSecretsVaultStore(new InMemoryTextFile { Content = "   " });
        Assert.Equal(SecretsVaultStatus.Empty, (await blank.LoadAsync(Pass, default)).Status);
    }

    [Fact]
    public async Task LoadAsync_ReturnsCorrupt_WhenNotAnEnvelope()
    {
        var store = new DriveSecretsVaultStore(new InMemoryTextFile { Content = "{\"not\":\"an envelope\"}" });
        Assert.Equal(SecretsVaultStatus.Corrupt, (await store.LoadAsync(Pass, default)).Status);

        var garbage = new DriveSecretsVaultStore(new InMemoryTextFile { Content = "not json at all" });
        Assert.Equal(SecretsVaultStatus.Corrupt, (await garbage.LoadAsync(Pass, default)).Status);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheWholeVault()
    {
        var file = new InMemoryTextFile();
        var store = new DriveSecretsVaultStore(file);
        var vault = SecretsVault.Empty with
        {
            Claude = new ClaudeSecrets("sk-claude"),
            Gemini = new GeminiSecrets("g-key"),
            Plaid = new PlaidSecrets("cid", "secret", "production")
            {
                Items = [new PlaidItem("item-1", "access-1", "Chase")]
            },
            GitHubUpdateToken = "ghp_token"
        };

        await store.SaveAsync(vault, Pass, default);

        // The file on Drive must be an opaque envelope, never the plaintext secrets.
        Assert.DoesNotContain("sk-claude", file.Content);
        Assert.DoesNotContain("access-1", file.Content);

        var result = await store.LoadAsync(Pass, default);
        Assert.Equal(SecretsVaultStatus.Loaded, result.Status);
        Assert.Equal("sk-claude", result.Vault.Claude.ApiKey);
        Assert.Equal("g-key", result.Vault.Gemini.ApiKey);
        Assert.Equal("production", result.Vault.Plaid.Environment);
        Assert.Equal("access-1", Assert.Single(result.Vault.Plaid.Items).AccessToken);
        Assert.Equal("ghp_token", result.Vault.GitHubUpdateToken);
    }

    [Fact]
    public async Task LoadAsync_ReturnsWrongPassphrase_WhenPassphraseIsWrong()
    {
        var store = new DriveSecretsVaultStore(new InMemoryTextFile());
        await store.SaveAsync(SecretsVault.Empty with { Claude = new ClaudeSecrets("sk") }, "right-pass", default);

        var result = await store.LoadAsync("wrong-pass", default);
        Assert.Equal(SecretsVaultStatus.WrongPassphrase, result.Status);
    }

    [Fact]
    public async Task PerSectionMerge_TwoAppsPublishing_BothSurvive()
    {
        var file = new InMemoryTextFile();
        var store = new DriveSecretsVaultStore(file);

        // App A (Storyboarder) writes its section into an empty vault.
        await store.SaveAsync(SecretsVault.Empty with { Claude = new ClaudeSecrets("sk-claude") }, Pass, default);

        // App B (Ledger) loads, replaces only its own section, saves.
        var current = (await store.LoadAsync(Pass, default)).Vault;
        await store.SaveAsync(current with { Plaid = new PlaidSecrets("cid", "sec") }, Pass, default);

        var result = await store.LoadAsync(Pass, default);
        Assert.Equal("sk-claude", result.Vault.Claude.ApiKey);   // A's write preserved
        Assert.Equal("cid", result.Vault.Plaid.ClientId);        // B's write applied
    }
}
