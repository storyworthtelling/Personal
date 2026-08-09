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

    private const string Pass = "vault-passphrase";

    [Fact]
    public async Task PublishThenLoad_RoundTripsThroughTheService()
    {
        var store = new DriveSecretsVaultStore(new InMemoryTextFile());
        var service = new SecretsVaultService(store, () => Pass);

        var wrote = await service.PublishAsync(v => v with { Gemini = new GeminiSecrets("g-key") }, default);
        Assert.True(wrote);

        var result = await service.LoadAsync(default);
        Assert.Equal(SecretsVaultStatus.Loaded, result.Status);
        Assert.Equal("g-key", result.Vault.Gemini.ApiKey);
    }

    [Fact]
    public async Task Publish_IsNoOp_WithoutPassphrase()
    {
        var store = new DriveSecretsVaultStore(new InMemoryTextFile());
        var service = new SecretsVaultService(store, () => null);

        Assert.False(await service.PublishAsync(v => v with { Gemini = new GeminiSecrets("x") }, default));
        Assert.False(service.HasPassphrase);
        Assert.Equal(SecretsVaultStatus.Empty, (await service.LoadAsync(default)).Status);
    }

    [Fact]
    public async Task Publish_RefusesToOverwrite_WhenExistingVaultUnreadable()
    {
        var file = new InMemoryTextFile();
        var real = new SecretsVaultService(new DriveSecretsVaultStore(file), () => "real-pass");
        await real.PublishAsync(v => v with { Claude = new ClaudeSecrets("keep-me") }, default);

        // A different app with the wrong passphrase must not clobber the file.
        var wrong = new SecretsVaultService(new DriveSecretsVaultStore(file), () => "wrong-pass");
        Assert.False(await wrong.PublishAsync(v => v with { Gemini = new GeminiSecrets("intruder") }, default));

        var result = await real.LoadAsync(default);
        Assert.Equal("keep-me", result.Vault.Claude.ApiKey);
        Assert.Equal(string.Empty, result.Vault.Gemini.ApiKey);
    }
}
