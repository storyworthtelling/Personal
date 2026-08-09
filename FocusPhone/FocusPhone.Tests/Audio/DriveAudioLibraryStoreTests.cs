using FocusPhone.Domain.Audio;
using FocusPhone.Domain.Music;
using FocusPhone.Infrastructure.Podcasts;
using FocusPhone.Tests.Support;
using Xunit;

namespace FocusPhone.Tests.Audio;

public class DriveAudioLibraryStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static AudioLibrarySnapshot Sample() =>
        new(
            [new PodcastSubscription("https://feed/1", "Show One", T0, 0)],
            ["ep-a", "ep-b"],
            [new PlaybackProgress("ep-c", TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(40), T0)],
            new PlaybackSettingsSnapshot(
                new PlaybackSettings(1.5f, SkipSilence: true),
                new() { ["https://feed/1"] = new PlaybackSettings(2.0f) }),
            new EqualizerLibrary(
                new EqualizerSettings(true, [300, 0, -200]),
                [new EqualizerPreset("Voice", [0, 100, 0])]),
            LoudnessMillibels: 400);

    [Fact]
    public async Task Load_AbsentFile_ReturnsNull()
    {
        var store = new DriveAudioLibraryStore(new InMemoryDriveTextFile());
        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Load_BlankFile_ReturnsNull()
    {
        var store = new DriveAudioLibraryStore(new InMemoryDriveTextFile { Content = "   " });
        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Load_GarbledFile_ReturnsNull()
    {
        var store = new DriveAudioLibraryStore(new InMemoryDriveTextFile { Content = "{ not json" });
        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips()
    {
        var file = new InMemoryDriveTextFile();
        var store = new DriveAudioLibraryStore(file);

        await store.SaveAsync(Sample(), CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.True(Sample().ContentEquals(loaded!));
        Assert.Equal("Show One", loaded!.Subscriptions[0].Title);
        Assert.Equal(["ep-a", "ep-b"], loaded.ListenedEpisodeIds);
        Assert.Equal("ep-c", loaded.Progress[0].MediaId);
        Assert.Equal(1.5f, loaded.Settings!.Default.Speed);
        Assert.Equal(2.0f, loaded.Settings.For("https://feed/1").Speed);
        Assert.Equal([300, 0, -200], loaded.Equalizer!.Active.BandGains);
        Assert.Equal("Voice", loaded.Equalizer.Presets[0].Name);
        Assert.Equal(400, loaded.LoudnessMillibels);
    }
}
