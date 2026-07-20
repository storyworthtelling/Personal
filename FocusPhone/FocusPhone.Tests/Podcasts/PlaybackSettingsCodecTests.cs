using FocusPhone.Domain.Audio;
using FocusPhone.Infrastructure.Podcasts;
using Xunit;

namespace FocusPhone.Tests.Podcasts;

public class PlaybackSettingsCodecTests
{
    [Fact]
    public void EncodeThenDecode_RoundTripsDefaultAndOverrides()
    {
        var snapshot = PlaybackSettingsSnapshot.Empty()
            .With("https://a.com/feed", new PlaybackSettings(1.5f, SkipSilence: true))
            .With("https://b.com/feed", new PlaybackSettings(1.2f));

        var decoded = PlaybackSettingsCodec.Decode(PlaybackSettingsCodec.Encode(snapshot));

        Assert.Equal(1.5f, decoded.For("https://a.com/feed").Speed);
        Assert.True(decoded.For("https://a.com/feed").SkipSilence);
        Assert.Equal(1.2f, decoded.For("https://b.com/feed").Speed);
    }

    [Fact]
    public void For_FallsBackToDefaultWhenFeedHasNoOverride()
    {
        var snapshot = PlaybackSettingsSnapshot.Empty();

        Assert.Equal(1.0f, snapshot.For("https://unknown.com/feed").Speed);
    }

    [Fact]
    public void With_DoesNotMutateTheOriginalSnapshot()
    {
        var original = PlaybackSettingsSnapshot.Empty();

        original.With("https://a.com/feed", new PlaybackSettings(2.0f));

        Assert.Empty(original.PerFeed);
    }

    [Fact]
    public void Decode_EmptyOrNull_ReturnsEmptyWithNormalDefault()
    {
        var snapshot = PlaybackSettingsCodec.Decode(null);

        Assert.Equal(1.0f, snapshot.Default.Speed);
        Assert.Empty(snapshot.PerFeed);
    }
}
