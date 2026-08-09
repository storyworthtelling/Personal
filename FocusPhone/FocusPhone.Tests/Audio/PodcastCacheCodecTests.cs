using FocusPhone.Domain.Audio;
using FocusPhone.Infrastructure.Podcasts;
using Xunit;

namespace FocusPhone.Tests.Audio;

public class PodcastCacheCodecTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static PodcastCache Sample() =>
        new(
            [new Episode("e1", "https://feed/1", "First", T0, TimeSpan.FromMinutes(20), "https://audio/1.mp3")],
            new Dictionary<string, string> { ["https://feed/1"] = "Show One" });

    [Fact]
    public void Decode_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Same(PodcastCache.Empty, PodcastCacheCodec.Decode(null));
        Assert.Same(PodcastCache.Empty, PodcastCacheCodec.Decode(""));
    }

    [Fact]
    public void Decode_Garbled_ReturnsEmpty()
    {
        Assert.Same(PodcastCache.Empty, PodcastCacheCodec.Decode("{ not json"));
    }

    [Fact]
    public void EncodeThenDecode_RoundTripsEpisodesAndTitles()
    {
        var restored = PodcastCacheCodec.Decode(PodcastCacheCodec.Encode(Sample()));

        var episode = Assert.Single(restored.Episodes);
        Assert.Equal("e1", episode.Id);
        Assert.Equal("https://feed/1", episode.FeedId);
        Assert.Equal(TimeSpan.FromMinutes(20), episode.Duration);
        Assert.Equal("https://audio/1.mp3", episode.AudioUrl);
        Assert.Equal("Show One", restored.FeedTitles["https://feed/1"]);
    }
}
