using FocusPhone.Domain.Timeline;
using Xunit;

namespace FocusPhone.Tests.Timeline;

public class TimelineItemFormatterTests
{
    private static TimelineItem Item(TimelineItemKind kind, string? snippet) =>
        new(kind, "alice", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            snippet, Incoming: true, SourceId: "s1");

    [Theory]
    [InlineData("Missed call")]
    [InlineData("Incoming call (01:12)")]
    public void Describe_Call_UsesSnippetVerbatim(string snippet)
    {
        Assert.Equal(snippet, TimelineItemFormatter.Describe(Item(TimelineItemKind.Call, snippet)));
    }

    [Fact]
    public void Describe_Message_PrefixesText()
    {
        Assert.Equal("Text: hey", TimelineItemFormatter.Describe(Item(TimelineItemKind.Message, "hey")));
    }

    [Fact]
    public void Describe_Email_PrefixesEmail()
    {
        Assert.Equal("Email: Lunch plans",
            TimelineItemFormatter.Describe(Item(TimelineItemKind.Email, "Lunch plans")));
    }

    [Fact]
    public void Describe_Voicemail_PrefixesTranscription()
    {
        Assert.Equal("Voicemail: Hi, it's mom",
            TimelineItemFormatter.Describe(Item(TimelineItemKind.Voicemail, "Hi, it's mom")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Voicemail")]
    public void Describe_VoicemailWithoutTranscription_ReturnsLabel(string? snippet)
    {
        Assert.Equal("Voicemail", TimelineItemFormatter.Describe(Item(TimelineItemKind.Voicemail, snippet)));
    }

    [Theory]
    [InlineData(TimelineItemKind.Message, "Text")]
    [InlineData(TimelineItemKind.Email, "Email")]
    public void Describe_NullSnippet_ReturnsLabelOnly(TimelineItemKind kind, string expected)
    {
        Assert.Equal(expected, TimelineItemFormatter.Describe(Item(kind, null)));
    }

    [Fact]
    public void Describe_CallWithNullSnippet_ReturnsEmpty()
    {
        Assert.Equal("", TimelineItemFormatter.Describe(Item(TimelineItemKind.Call, null)));
    }
}
