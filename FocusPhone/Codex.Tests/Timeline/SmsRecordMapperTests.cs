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

    [Fact]
    public void MapMms_TwoAddresses_IsAnOrdinaryOneToOneThread()
    {
        var item = SmsRecordMapper.MapMms(
            "26", T0Seconds, messageBox: 1, address: "5551234567", text: "hi",
            threadId: 9, allAddresses: ["5551234567", "5559999999"]);

        Assert.NotNull(item);
        Assert.False(item.IsGroup);
        Assert.Null(item.GroupId);
        Assert.Empty(item.Participants);
    }

    [Fact]
    public void MapMms_MoreThanTwoAddresses_IsAGroupThreadedByProviderThreadId()
    {
        var item = SmsRecordMapper.MapMms(
            "27", T0Seconds, messageBox: 1, address: "5551234567", text: "group photo",
            threadId: 42, allAddresses: ["5551234567", "5559999999", "5558888888"]);

        Assert.NotNull(item);
        Assert.True(item.IsGroup);
        Assert.Equal("mms:42", item.GroupId);
        Assert.Equal(["5551234567", "5558888888", "5559999999"], item.Participants.OrderBy(k => k));
    }

    [Fact]
    public void MapMms_DuplicateOrUnnormalizedAddresses_DoNotInflateTheGroupCount()
    {
        // The carrier lists the sender in "to" as well as "from" on some networks; without
        // normalizing and de-duplicating first, that alone would misread a 1:1 text as a group.
        var item = SmsRecordMapper.MapMms(
            "28", T0Seconds, messageBox: 1, address: "5551234567", text: "hi",
            threadId: 5, allAddresses: ["+1 (555) 123-4567", "5551234567", "5559999999"]);

        Assert.NotNull(item);
        Assert.False(item.IsGroup);
    }

    [Fact]
    public void MapMms_InternationalAddress_ParticipantAddressesKeepsTheCountryCode()
    {
        // Participants (normalized) keeps only the last ten digits — dropping the country
        // code a non-NANP number needs to actually be reachable. ParticipantAddresses must
        // carry the original spelling through untouched so a reply can still dial it.
        var item = SmsRecordMapper.MapMms(
            "30", T0Seconds, messageBox: 1, address: "5551234567", text: "group photo",
            threadId: 77, allAddresses: ["5551234567", "5559999999", "+447911123456"]);

        Assert.NotNull(item);
        Assert.True(item.IsGroup);
        Assert.Equal(3, item.Participants.Count);
        Assert.Equal(3, item.ParticipantAddresses.Count);
        Assert.Contains("+447911123456", item.ParticipantAddresses);
        Assert.DoesNotContain("7911123456", item.ParticipantAddresses);
    }

    [Fact]
    public void MapMms_ParticipantsAndParticipantAddresses_AreSameLengthAndOrder()
    {
        var item = SmsRecordMapper.MapMms(
            "31", T0Seconds, messageBox: 1, address: "5551234567", text: "hi",
            threadId: 78, allAddresses: ["+1 (555) 123-4567", "5559999999", "+447911123456"]);

        Assert.NotNull(item);
        Assert.Equal(item.Participants.Count, item.ParticipantAddresses.Count);
        for (var i = 0; i < item.Participants.Count; i++)
        {
            Assert.Equal(ContactKeyNormalizer.NormalizePhone(item.ParticipantAddresses[i]), item.Participants[i]);
        }
    }

    [Fact]
    public void MapMms_CarriesAttachmentsThrough()
    {
        var attachment = new AttachmentRef("part:1", "photo.jpg", "image/jpeg", 204_800);

        var item = SmsRecordMapper.MapMms(
            "29", T0Seconds, messageBox: 1, address: "5551234567", text: null,
            attachments: [attachment]);

        Assert.NotNull(item);
        Assert.Equal("Photo", item.Snippet);
        Assert.Single(item.Attachments);
        Assert.Equal("photo.jpg", item.Attachments[0].FileName);
    }

    // With this phone's own number known it is left out of a group's members: it is not
    // someone to show in the header, and a group reply sent to it would come straight back.
    [Fact]
    public void MapMms_LeavesThisPhoneOutOfTheMembers()
    {
        var item = SmsRecordMapper.MapMms("31", T0Seconds, messageBox: 1, "5551234567", "hi",
            threadId: 12, allAddresses: ["5551234567", "+15550100009", "5559999999"],
            ownNumbers: ["(555) 010-0009"])!;

        Assert.True(item.IsGroup);
        Assert.Equal(["5551234567", "5559999999"], item.Participants);
        Assert.Equal(["5551234567", "5559999999"], item.ParticipantAddresses);
    }

    [Fact]
    public void MapMms_OneToOneWithThisPhoneListed_IsNotAGroup()
    {
        var item = SmsRecordMapper.MapMms("32", T0Seconds, messageBox: 1, "5551234567", "hi",
            threadId: 13, allAddresses: ["5551234567", "5550100009"], ownNumbers: ["5550100009"])!;

        Assert.False(item.IsGroup);
    }
}
