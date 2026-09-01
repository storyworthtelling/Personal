using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Google Fi's "Voicemails by text" delivers the transcript as an ordinary SMS. That is
/// what retires the notification scraper: a text is a durable record Post already reads,
/// where a notification is a one-shot Post has to be running to catch.
///
/// It has to READ as a voicemail, not as a text, or it threads with the wrong half of that
/// person's correspondence — a voicemail belongs beside their calls.
///
/// The exact wording is Fi's and is NOT verified against a real message. The match is
/// therefore deliberately tolerant, and confirming the real shape is an item on the
/// on-device test plan.
/// </summary>
public class FiVoicemailTests
{
    private static readonly DateTimeOffset When = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Voicemail from (555) 010-2233: Hi, it's me, call me back.")]
    [InlineData("New voicemail from +15550102233 — Hi, it's me, call me back.")]
    [InlineData("Voicemail transcript: Hi, it's me, call me back.")]
    public void AFiVoicemailBecomesAVoicemail(string body)
    {
        var item = FiVoicemail.TryParse("+15550102233", body, When);

        Assert.NotNull(item);
        Assert.Equal(TimelineItemKind.Voicemail, item!.Kind);
    }

    // The transcript is what the owner reads; the wrapper Fi puts around it is not.
    [Fact]
    public void TheTranscriptIsKeptAndTheWrapperIsNot()
    {
        var item = FiVoicemail.TryParse(
            "+15550102233", "Voicemail from (555) 010-2233: Hi, it's me, call me back.", When);

        Assert.Equal("Hi, it's me, call me back.", item!.Snippet);
    }

    // It threads with the caller, which is the whole point of rendering it as a voicemail
    // rather than as a text from an unknown Fi number.
    [Fact]
    public void ItThreadsWithTheCallerNotWithFi()
    {
        var item = FiVoicemail.TryParse(
            "+15550102233", "Voicemail from (555) 010-2233: Hi.", When);

        Assert.Equal(ContactKeyNormalizer.NormalizePhone("+15550102233"), item!.ContactKey);
    }

    // An ordinary text is not a voicemail, however chatty. Getting this wrong would turn
    // real messages into voicemails, which is far worse than missing a transcript.
    [Theory]
    [InlineData("Hi, it's me, call me back.")]
    [InlineData("Did you get my voicemail?")]
    [InlineData("")]
    [InlineData(null)]
    public void AnOrdinaryTextIsLeftAlone(string? body)
    {
        Assert.Null(FiVoicemail.TryParse("+15550102233", body, When));
    }

    // Two voicemails from the same caller at different times are two items; the same one
    // seen twice is one. The source id has to carry both facts.
    [Fact]
    public void EachVoicemailHasItsOwnStableId()
    {
        var first = FiVoicemail.TryParse("+15550102233", "Voicemail from x: One.", When);
        var again = FiVoicemail.TryParse("+15550102233", "Voicemail from x: One.", When);
        var later = FiVoicemail.TryParse("+15550102233", "Voicemail from x: Two.", When.AddMinutes(5));

        Assert.Equal(first!.SourceId, again!.SourceId);
        Assert.NotEqual(first.SourceId, later!.SourceId);
    }

    // A transcript Fi could not produce still deserves a row: the owner needs to know
    // someone left a message, even unread.
    [Fact]
    public void AVoicemailWithNoTranscriptStillAppears()
    {
        var item = FiVoicemail.TryParse("+15550102233", "Voicemail from (555) 010-2233:", When);

        Assert.NotNull(item);
        Assert.False(string.IsNullOrWhiteSpace(item!.Snippet));
    }
}
