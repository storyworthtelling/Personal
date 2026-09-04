using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Timeline;

public class SmsRecordMapperTests
{
    private const long T0Millis = 1_750_000_000_000;
    private const long T0Seconds = 1_750_000_000;

    [Fact]
    public void Map_InboxRow_IncomingWithSmsPrefixedSourceId()
    {
        var item = SmsRecordMapper.Map("7", "5551234567", T0Millis, type: 1, body: "hey");

        Assert.NotNull(item);
        Assert.Equal(TimelineItemKind.Message, item.Kind);
        Assert.True(item.Incoming);
        Assert.Equal("sms:7", item.SourceId);
        Assert.Equal("hey", item.Snippet);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(T0Millis), item.TimestampUtc);
    }

    [Fact]
    public void Map_SentRow_Outgoing()
    {
        var item = SmsRecordMapper.Map("8", "5551234567", T0Millis, type: 2, body: "on my way");

        Assert.NotNull(item);
        Assert.False(item.Incoming);
    }

    [Theory]
    [InlineData(3)] // draft
    [InlineData(4)] // outbox
    [InlineData(5)] // failed
    [InlineData(6)] // queued
    [InlineData(0)] // unknown
    public void Map_NonInboxNonSentRow_ReturnsNull(int type)
    {
        Assert.Null(SmsRecordMapper.Map("9", "5551234567", T0Millis, type, body: "draft text"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Map_EmptyAddress_ReturnsNull(string? address)
    {
        Assert.Null(SmsRecordMapper.Map("10", address, T0Millis, type: 1, body: "hi"));
    }

    [Fact]
    public void Map_NormalizesPhoneContactKey()
    {
        var item = SmsRecordMapper.Map("11", "+1 (555) 123-4567", T0Millis, type: 1, body: "hi");

        Assert.NotNull(item);
        Assert.Equal("5551234567", item.ContactKey);
    }

    [Fact]
    public void MapMms_ConvertsSecondsToUtcTimestamp()
    {
        var item = SmsRecordMapper.MapMms("20", T0Seconds, messageBox: 1, "5551234567", "photo caption");

        Assert.NotNull(item);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(T0Seconds), item.TimestampUtc);
    }

    [Fact]
    public void MapMms_InboxAndSentBoxes_SetDirection()
    {
        var inbox = SmsRecordMapper.MapMms("21", T0Seconds, messageBox: 1, "5551234567", "in");
        var sent = SmsRecordMapper.MapMms("22", T0Seconds, messageBox: 2, "5551234567", "out");

        Assert.NotNull(inbox);
        Assert.True(inbox.Incoming);
        Assert.Equal("mms:21", inbox.SourceId);
        Assert.NotNull(sent);
        Assert.False(sent.Incoming);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MapMms_MissingText_UsesMmsPlaceholder(string? text)
    {
        var item = SmsRecordMapper.MapMms("23", T0Seconds, messageBox: 1, "5551234567", text);

        Assert.NotNull(item);
        Assert.Equal("[MMS]", item.Snippet);
    }

    [Theory]
    [InlineData(3)] // draft
    [InlineData(4)] // outbox
    [InlineData(0)] // unknown
    public void MapMms_UnknownBox_ReturnsNull(int messageBox)
    {
        Assert.Null(SmsRecordMapper.MapMms("24", T0Seconds, messageBox, "5551234567", "x"));
    }

    [Fact]
    public void MapMms_EmptyAddress_ReturnsNull()
    {
        Assert.Null(SmsRecordMapper.MapMms("25", T0Seconds, messageBox: 1, address: null, text: "x"));
    }
}
