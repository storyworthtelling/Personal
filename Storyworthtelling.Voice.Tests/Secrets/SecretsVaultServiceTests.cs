using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

public class SecretsVaultServiceTests
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

    // A store whose write always throws, standing in for the real failure that left the cutover vault empty:
    // a Drive write rejected because the full `drive` scope wasn't granted. Reads succeed so publish gets
    // past the load and fails only at the write, which is the case the hardening must surface.
    private sealed class WriteFailingStore : ISecretsVaultStore
    {
        public Task<SecretsVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken) =>
            Task.FromResult(SecretsVaultLoadResult.Empty);

        public Task SaveAsync(SecretsVault vault, string passphrase, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Drive returned 403 (insufficient scope).");
    }

    private const string Pass = "vault-passphrase";

    [Fact]
    public async Task PublishThenLoad_RoundTripsThroughTheService()
    {
        var store = new DriveSecretsVaultStore(new InMemoryTextFile());
        var service = new SecretsVaultService(store, () => Pass);

        var published = await service.PublishAsync(v => v with { Gemini = new GeminiSecrets("g-key") }, default);
        Assert.True(published.Succeeded);
        Assert.False(published.NeedsAttention);

        var result = await service.LoadAsync(default);
        Assert.Equal(SecretsVaultStatus.Loaded, result.Status);
        Assert.Equal("g-key", result.Vault.Gemini.ApiKey);
    }

    [Fact]
    public async Task Publish_IsNoOp_WithoutPassphrase()
    {
        var store = new DriveSecretsVaultStore(new InMemoryTextFile());
        var service = new SecretsVaultService(store, () => null);

        var result = await service.PublishAsync(v => v with { Gemini = new GeminiSecrets("x") }, default);
        Assert.Equal(SecretsVaultPublishStatus.NoPassphrase, result.Status);
        Assert.False(result.Succeeded);
        // No passphrase is a quiet, expected state — the app just isn't in the vault yet.
        Assert.False(result.NeedsAttention);
        Assert.False(service.HasPassphrase);
        Assert.Equal(SecretsVaultStatus.Empty, (await service.LoadAsync(default)).Status);
    }

    [Fact]
    public async Task Publish_RefusesToOverwrite_WhenExistingVaultUnreadable()
    {
        var file = new InMemoryTextFile();
        var real = new SecretsVaultService(new DriveSecretsVaultStore(file), () => "real-pass");
        await real.PublishAsync(v => v with { Claude = new ClaudeSecrets("keep-me") }, default);

        // A different app with the wrong passphrase must not clobber the file — and must be told so.
        var wrong = new SecretsVaultService(new DriveSecretsVaultStore(file), () => "wrong-pass");
        var result = await wrong.PublishAsync(v => v with { Gemini = new GeminiSecrets("intruder") }, default);
        Assert.Equal(SecretsVaultPublishStatus.VaultUnreadable, result.Status);
        Assert.True(result.NeedsAttention);
        Assert.NotEqual(string.Empty, result.Message);

        var reload = await real.LoadAsync(default);
        Assert.Equal("keep-me", reload.Vault.Claude.ApiKey);
        Assert.Equal(string.Empty, reload.Vault.Gemini.ApiKey);
    }

    [Fact]
    public async Task Publish_SurfacesWriteFailure_InsteadOfThrowing()
    {
        // The failure mode behind the empty cutover vault: a passphrase is set, the read succeeds, but the
        // Drive write is rejected. It must come back as a value the caller can show — not an escaping exception.
        var service = new SecretsVaultService(new WriteFailingStore(), () => Pass);

        var result = await service.PublishAsync(v => v with { Nyt = new NytSecrets("cookie", "key") }, default);

        Assert.Equal(SecretsVaultPublishStatus.WriteFailed, result.Status);
        Assert.True(result.NeedsAttention);
        Assert.False(result.Succeeded);
        Assert.Contains("403", result.Message);
    }
}
