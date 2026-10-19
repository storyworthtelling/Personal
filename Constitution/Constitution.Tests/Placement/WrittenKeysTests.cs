using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;

namespace Constitution.Tests.Placement;

/// <summary>
/// The durable record of which keys have been written. It is the only way a deleted event can be told from
/// one that was never made, so it has two rules: it is only ever grown, and a record that cannot be read is
/// never mistaken for an empty one.
/// </summary>
public class WrittenKeysTests
{
    private static readonly PlacementKey SamStrength = PlacementKey.ForWorkout("Sam", SessionKind.Strength, 105703, 0);
    private static readonly PlacementKey LeeCook = PlacementKey.ForHousehold("Lee", 105703, "dinner-a");

    [Fact]
    public void WithAddsAKeyAndLeavesTheOriginal()
    {
        var empty = WrittenKeys.Empty;

        var one = empty.With(SamStrength);

        Assert.True(one.Contains(SamStrength));
        Assert.False(empty.Contains(SamStrength));
        Assert.Empty(empty.Keys);
        Assert.Single(one.Keys);
    }

    [Fact]
    public void AKeyIsHeldOnce()
    {
        var keys = WrittenKeys.Empty.With(SamStrength).With(SamStrength);

        Assert.Single(keys.Keys);
    }

    [Fact]
    public void TheCodecWritesTheFrozenShape()
    {
        var keys = WrittenKeys.Empty.With(SamStrength).With(LeeCook);

        // Sorted, so the same record is the same text however it was built.
        Assert.Equal(
            "{\"version\":1,\"written\":[\"c1:lee:household:105703:dinner-a\",\"c1:sam:strength:105703:0\"]}",
            WrittenKeysCodec.Encode(keys));
        Assert.Equal(
            WrittenKeysCodec.Encode(WrittenKeys.Empty.With(LeeCook).With(SamStrength)),
            WrittenKeysCodec.Encode(keys));
        Assert.Equal("{\"version\":1,\"written\":[]}", WrittenKeysCodec.Encode(WrittenKeys.Empty));
    }

    [Fact]
    public void TheCodecRoundTrips()
    {
        var keys = WrittenKeys.Empty.With(SamStrength).With(LeeCook);

        var read = WrittenKeysCodec.Decode(WrittenKeysCodec.Encode(keys));

        Assert.True(read.IsReadable);
        Assert.Null(read.UnreadableBecause);
        Assert.Equal(2, read.Keys.Keys.Count);
        Assert.True(read.Keys.Contains(SamStrength));
        Assert.True(read.Keys.Contains(LeeCook));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"version\":1,\"written\":[]}")]
    public void NothingStoredYetIsAnEmptyRecord(string? stored)
    {
        var read = WrittenKeysCodec.Decode(stored);

        Assert.True(read.IsReadable);
        Assert.Empty(read.Keys.Keys);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ \"version\": 1, \"written\": [")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"written\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":2,\"written\":[]}")]
    [InlineData("{\"version\":\"1\",\"written\":[]}")]
    [InlineData("{\"version\":1,\"written\":\"c1:sam:strength:5:0\"}")]
    [InlineData("{\"version\":1,\"written\":[1]}")]
    [InlineData("{\"version\":1,\"written\":[null]}")]
    [InlineData("{\"version\":1,\"written\":[\"garbage\"]}")]
    public void ARecordThatCannotBeReadIsNeverAnEmptyOne(string stored)
    {
        var read = WrittenKeysCodec.Decode(stored);

        Assert.False(read.IsReadable);
        Assert.False(string.IsNullOrWhiteSpace(read.UnreadableBecause));
        Assert.Empty(read.Keys.Keys);
    }

    [Fact]
    public void ADuplicateInTheFileIsHeldOnce()
    {
        var read = WrittenKeysCodec.Decode("{\"version\":1,\"written\":[\"c1:sam:strength:5:0\",\"c1:sam:strength:5:0\"]}");

        Assert.True(read.IsReadable);
        Assert.Single(read.Keys.Keys);
    }

    [Fact]
    public void AnEmptyKeyIsNotRecorded()
    {
        Assert.Throws<ArgumentException>(() => WrittenKeys.Empty.With(default(PlacementKey)));
    }

    [Fact]
    public void KeysComeBackInAFixedOrder()
    {
        var keys = WrittenKeys.Empty.With(SamStrength).With(LeeCook);

        Assert.Equal([LeeCook, SamStrength], keys.Keys);
    }
}
