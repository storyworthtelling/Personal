using FocusPhone.Domain.Music;
using FocusPhone.Infrastructure.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class ListeningEventCodecTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 20, 30, 0, TimeSpan.Zero);

    [Fact]
    public void RoundTrip_PreservesEvents()
    {
        var events = new[]
        {
            new ListeningEvent("t1", "Jazz", "Bill Evans", "Bill Evans", ListeningSignal.Completed, 20, "s1", Now),
            new ListeningEvent("t2", "Rock", "Nirvana", "Nirvana", ListeningSignal.Skipped, 8, "s2", Now.AddMinutes(5)),
        };

        var decoded = ListeningEventCodec.Decode(ListeningEventCodec.Encode(events));

        Assert.Equal(2, decoded.Count);
        Assert.Equal("t1", decoded[0].TrackId);
        Assert.Equal(ListeningSignal.Skipped, decoded[1].Signal);
        Assert.Equal(8, decoded[1].LocalHour);
        Assert.Equal("s2", decoded[1].SessionId);
    }

    [Fact]
    public void Decode_Null_ReturnsEmpty() => Assert.Empty(ListeningEventCodec.Decode(null));

    [Fact]
    public void Decode_Blank_ReturnsEmpty() => Assert.Empty(ListeningEventCodec.Decode("   "));

    [Fact]
    public void Decode_Garbled_ReturnsEmpty() => Assert.Empty(ListeningEventCodec.Decode("{not json"));
}
