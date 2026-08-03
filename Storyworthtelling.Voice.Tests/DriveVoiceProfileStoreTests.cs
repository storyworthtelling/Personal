using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

public class DriveVoiceProfileStoreTests
{
    // An in-memory stand-in for the Drive-backed text file, so the store's own logic (serialization,
    // absent-file handling, round-trip) is tested without touching Google.
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
    public async Task LoadAsync_ReturnsEmpty_WhenNoProfileHasBeenSavedYet()
    {
        var store = new DriveVoiceProfileStore(new InMemoryTextFile { Content = null });

        Assert.Equal(VoiceProfile.Empty, await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_WhenTheFileIsBlankOrGarbled()
    {
        Assert.Equal(VoiceProfile.Empty,
            await new DriveVoiceProfileStore(new InMemoryTextFile { Content = "   " }).LoadAsync(CancellationToken.None));
        Assert.Equal(VoiceProfile.Empty,
            await new DriveVoiceProfileStore(new InMemoryTextFile { Content = "not json" }).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheProfile()
    {
        var file = new InMemoryTextFile();
        var store = new DriveVoiceProfileStore(file);
        var profile = new VoiceProfile(
            "Wry and warm.", ["wry", "warm"], ["At last, the gate."], ["hype"], ["em-dash asides"], DocumentsAnalyzed: 3);

        await store.SaveAsync(profile, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        // Compared field by field: record equality would compare the list references, and a JSON
        // round-trip yields fresh List instances, so only the contents can be asserted equal.
        Assert.Equal(profile.Summary, loaded.Summary);
        Assert.Equal(profile.Traits, loaded.Traits);
        Assert.Equal(profile.Examples, loaded.Examples);
        Assert.Equal(profile.Avoid, loaded.Avoid);
        Assert.Equal(profile.SignatureMoves, loaded.SignatureMoves);
        Assert.Equal(profile.DocumentsAnalyzed, loaded.DocumentsAnalyzed);
    }

    [Fact]
    public async Task LoadAsync_DefaultsNewLists_ForAProfileSavedBeforeTheFieldExisted()
    {
        // A profile written before signatureMoves was added has no such field; it must load as an empty
        // list, never null, so every consumer can enumerate it without a null-check.
        var legacy = """{"summary":"Plain-spoken.","traits":["plain"],"examples":[],"avoid":[],"documentsAnalyzed":2}""";
        var store = new DriveVoiceProfileStore(new InMemoryTextFile { Content = legacy });

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded.SignatureMoves);
        Assert.Empty(loaded.SignatureMoves);
        Assert.Equal("Plain-spoken.", loaded.Summary);
    }
}
