using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;
using Xunit;

namespace Codex.Tests.Podcasts;

public class PodcastProgressCodecTests
{
    private static readonly DateTimeOffset When = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EncodeThenDecode_RoundTripsProgress()
    {
        var progress = new PlaybackProgress[]
        {
            new("ep1", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(40), When),
            new("ep2", TimeSpan.FromSeconds(90), null, When.AddHours(1)),
        };

        var decoded = PodcastProgressCodec.Decode(PodcastProgressCodec.Encode(progress));

        Assert.Equal(progress, decoded);
    }

    [Fact]
    public void Decode_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(PodcastProgressCodec.Decode(null));
        Assert.Empty(PodcastProgressCodec.Decode(""));
    }

    [Fact]
    public void Decoded_RebuildsAWorkingBook()
    {
        var book = new PlaybackProgressBook();
        book.Record("ep1", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(40), When);

        var restored = new PlaybackProgressBook(PodcastProgressCodec.Decode(PodcastProgressCodec.Encode(book.Export())));

        Assert.Equal(TimeSpan.FromMinutes(5), restored.Resume("ep1"));
    }
}
