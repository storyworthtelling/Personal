using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

public class DriveVoiceConfigTests
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

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_WhenNothingSavedOrGarbled()
    {
        Assert.True((await new DriveVoiceConfigStore(new InMemoryTextFile { Content = null }).LoadAsync(default)).IsEmpty);
        Assert.True((await new DriveVoiceConfigStore(new InMemoryTextFile { Content = "  " }).LoadAsync(default)).IsEmpty);
        Assert.True((await new DriveVoiceConfigStore(new InMemoryTextFile { Content = "nonsense" }).LoadAsync(default)).IsEmpty);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheKeyAndModels()
    {
        var store = new DriveVoiceConfigStore(new InMemoryTextFile());
        await store.SaveAsync(new VoiceConfig("sk-shared", "model-x", "model-fast"), default);

        var loaded = await store.LoadAsync(default);
        Assert.Equal("sk-shared", loaded.ApiKey);
        Assert.Equal("model-x", loaded.Model);
        Assert.Equal("model-fast", loaded.FastModel);
        Assert.False(loaded.IsEmpty);
    }

    [Fact]
    public async Task ConfigWithOnlyAKey_KeepsDefaultModels()
    {
        var store = new DriveVoiceConfigStore(new InMemoryTextFile { Content = "{\"apiKey\":\"sk-only\"}" });
        var loaded = await store.LoadAsync(default);

        Assert.Equal("sk-only", loaded.ApiKey);
        Assert.Equal(VoiceConfig.Empty.Model, loaded.Model);       // default model tier
        Assert.Equal(VoiceConfig.Empty.FastModel, loaded.FastModel);
    }

    [Fact]
    public async Task DriveVoiceOptions_LoadsTheKeyFromDrive_OnRefresh()
    {
        var store = new DriveVoiceConfigStore(new InMemoryTextFile { Content = "{\"apiKey\":\"sk-live\"}" });
        var options = new DriveVoiceOptions(store);

        Assert.Equal(string.Empty, options.Value.ApiKey); // empty until refreshed

        await options.RefreshAsync(default);

        Assert.Equal("sk-live", options.Value.ApiKey);
    }
}
