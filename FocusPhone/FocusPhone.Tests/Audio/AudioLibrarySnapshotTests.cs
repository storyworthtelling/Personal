using FocusPhone.Domain.Audio;
using FocusPhone.Domain.Music;
using Xunit;

namespace FocusPhone.Tests.Audio;

public class AudioLibrarySnapshotTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static PodcastSubscription Sub(string url, int order = 0) =>
        new(url, Title: url, AddedUtc: T0, SortOrder: order);

    private static PlaybackProgress Prog(string id) =>
        new(id, Position: TimeSpan.FromMinutes(1), Duration: TimeSpan.FromMinutes(30), UpdatedUtc: T0);

    [Fact]
    public void Empty_IsEmpty_AndHasNoSubscriptions()
    {
        Assert.True(AudioLibrarySnapshot.Empty.IsEmpty);
        Assert.False(AudioLibrarySnapshot.Empty.HasSubscriptions);
    }

    [Fact]
    public void WithOnlyListened_IsNotEmpty_ButHasNoSubscriptions()
    {
        var snapshot = new AudioLibrarySnapshot([], ["a"], []);

        Assert.False(snapshot.IsEmpty);
        Assert.False(snapshot.HasSubscriptions);
    }

    [Fact]
    public void ContentEquals_IgnoresListenedOrder()
    {
        var a = new AudioLibrarySnapshot([Sub("f")], ["x", "y"], []);
        var b = new AudioLibrarySnapshot([Sub("f")], ["y", "x"], []);

        Assert.True(a.ContentEquals(b));
    }

    [Fact]
    public void ContentEquals_IgnoresSubscriptionOrder()
    {
        var a = new AudioLibrarySnapshot([Sub("f1", 0), Sub("f2", 1)], [], []);
        var b = new AudioLibrarySnapshot([Sub("f2", 1), Sub("f1", 0)], [], []);

        Assert.True(a.ContentEquals(b));
    }

    [Fact]
    public void ContentEquals_DetectsDifferentProgressPosition()
    {
        var a = new AudioLibrarySnapshot([Sub("f")], [], [Prog("e")]);
        var b = new AudioLibrarySnapshot([Sub("f")], [],
            [new PlaybackProgress("e", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), T0)]);

        Assert.False(a.ContentEquals(b));
    }

    [Fact]
    public void ContentEquals_DetectsExtraListenedId()
    {
        var a = new AudioLibrarySnapshot([Sub("f")], ["x"], []);
        var b = new AudioLibrarySnapshot([Sub("f")], ["x", "y"], []);

        Assert.False(a.ContentEquals(b));
    }

    [Fact]
    public void ContentEquals_NullSettings_EqualsDefaultSettings()
    {
        var withNull = new AudioLibrarySnapshot([Sub("f")], [], []);
        var withDefault = new AudioLibrarySnapshot([Sub("f")], [], [], PlaybackSettingsSnapshot.Empty());

        Assert.True(withNull.ContentEquals(withDefault));
    }

    [Fact]
    public void ContentEquals_DetectsDifferentDefaultSpeed()
    {
        var slow = new AudioLibrarySnapshot([Sub("f")], [], [],
            new PlaybackSettingsSnapshot(new PlaybackSettings(1.0f), []));
        var fast = new AudioLibrarySnapshot([Sub("f")], [], [],
            new PlaybackSettingsSnapshot(new PlaybackSettings(1.5f), []));

        Assert.False(slow.ContentEquals(fast));
    }

    [Fact]
    public void ContentEquals_DetectsDifferentPerFeedOverride()
    {
        var a = new AudioLibrarySnapshot([Sub("f")], [], [],
            new PlaybackSettingsSnapshot(new PlaybackSettings(), new() { ["f"] = new PlaybackSettings(2.0f) }));
        var b = new AudioLibrarySnapshot([Sub("f")], [], [],
            new PlaybackSettingsSnapshot(new PlaybackSettings(), new() { ["f"] = new PlaybackSettings(1.2f) }));

        Assert.False(a.ContentEquals(b));
    }

    [Fact]
    public void ContentEquals_DetectsDifferentEqualizerGains()
    {
        var flat = new AudioLibrarySnapshot([Sub("f")], [], [], null,
            new EqualizerLibrary(new EqualizerSettings(true, [0, 0, 0]), []));
        var bumped = new AudioLibrarySnapshot([Sub("f")], [], [], null,
            new EqualizerLibrary(new EqualizerSettings(true, [300, 0, 0]), []));

        Assert.False(flat.ContentEquals(bumped));
    }

    [Fact]
    public void ContentEquals_MatchesEqualizerWithReorderedPresets()
    {
        var a = new AudioLibrarySnapshot([Sub("f")], [], [], null,
            new EqualizerLibrary(new EqualizerSettings(false, [0, 0]),
                [new EqualizerPreset("Bass", [100, 0]), new EqualizerPreset("Voice", [0, 100])]));
        var b = new AudioLibrarySnapshot([Sub("f")], [], [], null,
            new EqualizerLibrary(new EqualizerSettings(false, [0, 0]),
                [new EqualizerPreset("Voice", [0, 100]), new EqualizerPreset("Bass", [100, 0])]));

        Assert.True(a.ContentEquals(b));
    }

    [Fact]
    public void ContentEquals_DetectsDifferentLoudness()
    {
        var quiet = new AudioLibrarySnapshot([Sub("f")], [], [], null, null, LoudnessMillibels: 0);
        var loud = new AudioLibrarySnapshot([Sub("f")], [], [], null, null, LoudnessMillibels: 500);

        Assert.False(quiet.ContentEquals(loud));
    }
}
