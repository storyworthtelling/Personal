using FocusPhone.Domain.Timeline;
using Xunit;

namespace FocusPhone.Tests.Timeline;

public class CallRecordMapperTests
{
    private const long T0Millis = 1_750_000_000_000;

    private static TimelineItem Map(int type, long durationSeconds = 0, string number = "5551234567") =>
        CallRecordMapper.Map("42", number, T0Millis, type, durationSeconds);

    [Fact]
    public void Map_VoicemailType_ProducesVoicemailKindIncoming()
    {
        var item = Map(type: 4);

        Assert.Equal(TimelineItemKind.Voicemail, item.Kind);
        Assert.True(item.Incoming);
        Assert.Equal("Voicemail", item.Snippet);
        Assert.Equal("42", item.SourceId);
    }

    [Fact]
    public void Map_MissedCall_Snippet()
    {
        var item = Map(type: 3);

        Assert.Equal(TimelineItemKind.Call, item.Kind);
        Assert.Equal("Missed call", item.Snippet);
        Assert.True(item.Incoming);
    }

    [Fact]
    public void Map_Rejected_Snippet()
    {
        var item = Map(type: 5);

        Assert.Equal("Declined call", item.Snippet);
        Assert.True(item.Incoming);
    }

    [Fact]
    public void Map_IncomingWithDuration_Snippet()
    {
        var item = Map(type: 1, durationSeconds: 72);

        Assert.Equal("Incoming call (01:12)", item.Snippet);
        Assert.True(item.Incoming);
    }

    [Fact]
    public void Map_Outgoing_SnippetAndDirection()
    {
        var item = Map(type: 2, durationSeconds: 5);

        Assert.Equal("Outgoing call (00:05)", item.Snippet);
        Assert.False(item.Incoming);
    }

    [Fact]
    public void Map_NormalizesNumberToContactKey()
    {
        var item = Map(type: 1, number: "+1 (555) 123-4567");

        Assert.Equal("5551234567", item.ContactKey);
    }

    [Fact]
    public void Map_UsesCallLogIdAsSourceIdAndMillisAsTimestamp()
    {
        var item = Map(type: 1);

        Assert.Equal("42", item.SourceId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(T0Millis), item.TimestampUtc);
    }
}
