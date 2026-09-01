using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;
using Xunit;

namespace Codex.Tests.Podcasts;

public class QueueCodecTests
{
    [Fact]
    public void EncodeThenDecode_RoundTripsOrderedIds()
    {
        var ids = new[] { "ep-3", "ep-1", "ep-2" };

        var decoded = QueueCodec.Decode(QueueCodec.Encode(ids));

        Assert.Equal(ids, decoded);
    }

    [Fact]
    public void Decoded_RebuildsAWorkingQueue_PreservingOrder()
    {
        var queue = new PlaybackQueue(["a", "b", "c"]);
        queue.Move(0, 2);

        var restored = new PlaybackQueue(QueueCodec.Decode(QueueCodec.Encode(queue.Export())));

        Assert.Equal(["b", "c", "a"], restored.Items);
    }

    [Fact]
    public void Decode_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(QueueCodec.Decode(null));
        Assert.Empty(QueueCodec.Decode(""));
    }
}
