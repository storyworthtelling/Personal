using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

/// <summary>
/// The live voice options every reader app holds: empty until refreshed, then whatever the config store
/// holds. The store is the vault's (<c>VaultVoiceConfigStore</c>); the plaintext Drive config file it
/// replaced, and its store, are gone (orphan audit, W7.2).
/// </summary>
public class DriveVoiceOptionsTests
{
    private sealed class FixedConfig(VoiceConfig config) : IVoiceConfigStore
    {
        public Task<VoiceConfig> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(config);

        public Task SaveAsync(VoiceConfig value, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task TheKeyArrivesOnRefresh()
    {
        var options = new DriveVoiceOptions(new FixedConfig(VoiceConfig.Empty with { ApiKey = "sk-live" }));

        Assert.Equal(string.Empty, options.Value.ApiKey);

        await options.RefreshAsync(default);

        Assert.Equal("sk-live", options.Value.ApiKey);
    }
}
