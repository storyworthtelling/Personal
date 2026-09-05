using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Timeline;

public class VoicemailNotificationParserTests
{
    private const long WhenMillis = 1_750_000_000_000;

    [Fact]
    public void TryParse_DialerVoicemailChannel_ReturnsVoicemailItem()
    {
        var item = VoicemailNotificationParser.TryParse(
            VoicemailNotificationParser.DialerPackage,
            channelId: "phone_voicemail_channel",
            title: "(555) 123-4567",
            text: "Hi, it's mom, call me back.",
            WhenMillis);

        Assert.NotNull(item);
        Assert.Equal(TimelineItemKind.Voicemail, item.Kind);
        Assert.True(item.Incoming);
        Assert.Equal("5551234567", item.ContactKey);
        Assert.Equal("Hi, it's mom, call me back.", item.Snippet);
        Assert.Equal($"vm:5551234567:{WhenMillis}", item.SourceId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(WhenMillis), item.TimestampUtc);
    }

    [Fact]
    public void TryParse_VoicemailTitleWithoutChannel_StillParses()
    {
        var item = VoicemailNotificationParser.TryParse(
            VoicemailNotificationParser.DialerPackage,
            channelId: null,
            title: "Voicemail from (555) 123-4567",
            text: "transcript",
            WhenMillis);

        Assert.NotNull(item);
        Assert.Equal("5551234567", item.ContactKey);
    }

    [Fact]
    public void TryParse_OtherPackage_ReturnsNull()
    {
        Assert.Null(VoicemailNotificationParser.TryParse(
            "com.google.android.apps.messaging",
            channelId: "voicemail",
            title: "Voicemail from Mom",
            text: "transcript",
            WhenMillis));
    }

    [Fact]
    public void TryParse_DialerMissedCallNotification_ReturnsNull()
    {
        Assert.Null(VoicemailNotificationParser.TryParse(
            VoicemailNotificationParser.DialerPackage,
            channelId: "phone_missed_call",
            title: "Missed call",
            text: "(555) 123-4567",
            WhenMillis));
    }

    [Fact]
    public void TryParse_StripsVoicemailFromPrefixAndKeysByName()
    {
        var item = VoicemailNotificationParser.TryParse(
            VoicemailNotificationParser.DialerPackage,
            channelId: null,
            title: "Voicemail from Mom",
            text: "call me",
            WhenMillis);

        Assert.NotNull(item);
        Assert.Equal("mom", item.ContactKey);
        Assert.Equal($"vm:mom:{WhenMillis}", item.SourceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_EmptyTranscription_SnippetIsVoicemailLabel(string? text)
    {
        var item = VoicemailNotificationParser.TryParse(
            VoicemailNotificationParser.DialerPackage,
            channelId: "voicemail",
            title: "(555) 123-4567",
            text,
            WhenMillis);

        Assert.NotNull(item);
        Assert.Equal("Voicemail", item.Snippet);
    }

    [Fact]
    public void TryParse_TitleIsJustVoicemail_FallsBackToVoicemailKey()
    {
        var item = VoicemailNotificationParser.TryParse(
            VoicemailNotificationParser.DialerPackage,
            channelId: "voicemail",
            title: "Voicemail",
            text: "transcript",
            WhenMillis);

        Assert.NotNull(item);
        Assert.Equal("voicemail", item.ContactKey);
    }
}
