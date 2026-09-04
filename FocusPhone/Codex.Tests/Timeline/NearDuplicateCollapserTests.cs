using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Timeline;

public class NearDuplicateCollapserTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private static TimelineItem Item(
        TimelineItemKind kind, string contactKey, string sourceId,
        string? snippet = "hey", bool incoming = true, int secondsAfterT0 = 0) =>
        new(kind, contactKey, T0.AddSeconds(secondsAfterT0), snippet, incoming, sourceId);

    [Fact]
    public void Collapse_DropsNotificationTextMatchingProviderText()
    {
        var provider = Item(TimelineItemKind.Message, "5551234567", "sms:1");
        var notification = Item(TimelineItemKind.Message, "5551234567", "5551234567:1750000000000",
            secondsAfterT0: 30);

        var result = NearDuplicateCollapser.Collapse([provider, notification]);

        var survivor = Assert.Single(result);
        Assert.Equal("sms:1", survivor.SourceId);
    }

    [Fact]
    public void Collapse_KeepsNotificationTextWithNoProviderMatch()
    {
        // The RCS case: the message never lands in the SMS provider.
        var notification = Item(TimelineItemKind.Message, "5551234567", "5551234567:1750000000000",
            snippet: "rcs only");
        var unrelatedProvider = Item(TimelineItemKind.Message, "5551234567", "sms:1", snippet: "other text");

        var result = NearDuplicateCollapser.Collapse([unrelatedProvider, notification]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Collapse_MatchesNameKeyedNotificationAgainstNumberKeyedProviderItem()
    {
        var provider = Item(TimelineItemKind.Message, "5551234567", "sms:1");
        var notification = Item(TimelineItemKind.Message, "mom", "mom:1750000000000", secondsAfterT0: 45);

        var result = NearDuplicateCollapser.Collapse([provider, notification]);

        var survivor = Assert.Single(result);
        Assert.Equal("sms:1", survivor.SourceId);
    }

    [Fact]
    public void Collapse_KeepsBothWhenSnippetsDiffer()
    {
        var provider = Item(TimelineItemKind.Message, "5551234567", "sms:1", snippet: "hey");
        var notification = Item(TimelineItemKind.Message, "5551234567", "5551234567:1", snippet: "hey there");

        Assert.Equal(2, NearDuplicateCollapser.Collapse([provider, notification]).Count);
    }

    [Fact]
    public void Collapse_KeepsBothWhenOutsideWindow()
    {
        var provider = Item(TimelineItemKind.Message, "5551234567", "sms:1");
        var notification = Item(TimelineItemKind.Message, "5551234567", "5551234567:1",
            secondsAfterT0: 5 * 60);

        Assert.Equal(2, NearDuplicateCollapser.Collapse([provider, notification]).Count);
    }

    [Fact]
    public void Collapse_KeepsBothWhenDirectionsDiffer()
    {
        var provider = Item(TimelineItemKind.Message, "5551234567", "sms:1", incoming: false);
        var notification = Item(TimelineItemKind.Message, "5551234567", "5551234567:1", incoming: true);

        Assert.Equal(2, NearDuplicateCollapser.Collapse([provider, notification]).Count);
    }

    [Fact]
    public void Collapse_MatchesSnippetsIgnoringSurroundingWhitespace()
    {
        var provider = Item(TimelineItemKind.Message, "5551234567", "sms:1", snippet: "hey");
        var notification = Item(TimelineItemKind.Message, "5551234567", "5551234567:1", snippet: " hey ");

        var survivor = Assert.Single(NearDuplicateCollapser.Collapse([provider, notification]));
        Assert.Equal("sms:1", survivor.SourceId);
    }

    [Fact]
    public void Collapse_PrefersTranscribedVoicemailOverCallLogVoicemail()
    {
        var callLog = Item(TimelineItemKind.Voicemail, "5551234567", "42", snippet: "Voicemail");
        var notification = Item(TimelineItemKind.Voicemail, "5551234567", "vm:5551234567:1",
            snippet: "Hi, it's mom", secondsAfterT0: 60);

        var survivor = Assert.Single(NearDuplicateCollapser.Collapse([callLog, notification]));
        Assert.Equal("vm:5551234567:1", survivor.SourceId);
    }

    [Fact]
    public void Collapse_MatchesNameKeyedVoicemailNotificationAgainstCallLogRow()
    {
        var callLog = Item(TimelineItemKind.Voicemail, "5551234567", "42", snippet: "Voicemail");
        var notification = Item(TimelineItemKind.Voicemail, "mom", "vm:mom:1", snippet: "call me");

        var survivor = Assert.Single(NearDuplicateCollapser.Collapse([callLog, notification]));
        Assert.Equal("vm:mom:1", survivor.SourceId);
    }

    [Fact]
    public void Collapse_KeepsCallLogVoicemailWithoutNotificationMatch()
    {
        var callLog = Item(TimelineItemKind.Voicemail, "5551234567", "42", snippet: "Voicemail");

        var survivor = Assert.Single(NearDuplicateCollapser.Collapse([callLog]));
        Assert.Equal("42", survivor.SourceId);
    }

    [Fact]
    public void Collapse_KeepsCallLogVoicemailWhenNotificationOutsideWindow()
    {
        var callLog = Item(TimelineItemKind.Voicemail, "5551234567", "42", snippet: "Voicemail");
        var notification = Item(TimelineItemKind.Voicemail, "5551234567", "vm:5551234567:1",
            snippet: "transcript", secondsAfterT0: 10 * 60);

        Assert.Equal(2, NearDuplicateCollapser.Collapse([callLog, notification]).Count);
    }

    [Fact]
    public void Collapse_LeavesCallsAndEmailsUntouched()
    {
        var call = Item(TimelineItemKind.Call, "5551234567", "42", snippet: "hey");
        var providerText = Item(TimelineItemKind.Message, "5551234567", "sms:1", snippet: "hey");
        var email = Item(TimelineItemKind.Email, "alice@example.com", "<msg-id@mail>", snippet: "hey");

        Assert.Equal(3, NearDuplicateCollapser.Collapse([call, providerText, email]).Count);
    }
}
