using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class SavedTidalTracksCodecTests
{
    private static Track Track(string id, string title) => new(
        id, title, "Some Artist", "Some Album", "Some Artist", 1, 1, "Rock", TimeSpan.FromMinutes(3),
        Availability: TrackAvailability.Streaming);

    [Fact]
    public void EncodeThenDecode_RoundTripsTracks()
    {
        var tracks = new[] { Track("tidal:1", "Song One"), Track("tidal:2", "Song Two") };

        var decoded = SavedTidalTracksCodec.Decode(SavedTidalTracksCodec.Encode(tracks));

        Assert.Equal(tracks, decoded);
    }

    [Fact]
    public void Decode_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(SavedTidalTracksCodec.Decode(null));
        Assert.Empty(SavedTidalTracksCodec.Decode(""));
    }

    [Fact]
    public void Decode_Garbled_ReturnsEmpty()
    {
        Assert.Empty(SavedTidalTracksCodec.Decode("not json"));
    }
}
