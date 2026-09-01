using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Third-party call recording is closed on Android — the mic route went in 10, the
/// accessibility route in May 2022 — so the transcript the design interview asked for is
/// not deliverable. What replaces it is the owner's own note on the call.
///
/// Google's Call Notes can fill it in where it exists, but it exports only through the
/// share sheet, so Post takes what arrives and falls back to manual entry. It does not
/// work on the 8a at all, which is precisely why the fallback is the default rather than
/// the exception.
/// </summary>
public class CallNoteTests
{
    private static readonly DateTimeOffset When = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANoteBecomesAnItemOnThatPersonsThread()
    {
        var note = CallNote.For("+15550102233", "Agreed he would come Tuesday.", When);

        Assert.Equal(ContactKeyNormalizer.NormalizePhone("+15550102233"), note.ContactKey);
        Assert.Equal(TimelineItemKind.Call, note.Kind);
        Assert.Contains("Tuesday", note.Snippet);
    }

    // A note is something the owner wrote, so it is theirs — outgoing — not something that
    // arrived. Marking it incoming would put it on the wrong side of the conversation.
    [Fact]
    public void ANoteIsNotIncoming()
    {
        Assert.False(CallNote.For("+15550102233", "Anything.", When).Incoming);
    }

    // Notes on the same call must not collide, and a note must not collide with the call
    // log row it sits beside.
    [Fact]
    public void EachNoteHasItsOwnId()
    {
        var first = CallNote.For("+15550102233", "One.", When);
        var second = CallNote.For("+15550102233", "Two.", When.AddMinutes(1));

        Assert.NotEqual(first.SourceId, second.SourceId);
        Assert.StartsWith("note:", first.SourceId);
    }

    // ---- what arrives from the share sheet ----

    // Call Notes shares plain text. Post has to find the number in it, because a share
    // carries no structure at all — and without a number the note has no thread to join.
    [Theory]
    [InlineData("Call with +1 555-010-2233\nAgreed he would come Tuesday.", "Agreed he would come Tuesday.")]
    [InlineData("+15550102233: Agreed he would come Tuesday.", "Agreed he would come Tuesday.")]
    public void ASharedNoteIsReadForItsNumberAndItsWords(string shared, string expectedText)
    {
        var parsed = CallNote.FromShare(shared, When);

        Assert.NotNull(parsed);
        Assert.Equal(ContactKeyNormalizer.NormalizePhone("+15550102233"), parsed!.ContactKey);
        Assert.Equal(expectedText, parsed.Snippet);
    }

    // A share with no number in it cannot be threaded, and guessing whose call it was
    // would be worse than asking. The screen falls back to manual entry.
    [Theory]
    [InlineData("Just some text with no number")]
    [InlineData("")]
    [InlineData(null)]
    public void AShareWithNoNumberIsNotANote(string? shared)
    {
        Assert.Null(CallNote.FromShare(shared, When));
    }
}
