using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Field notes are OBSERVATIONS, not failures — the numbers that decide what the deferred
/// work should be, gathered while the owner runs the test plan on a real phone with real
/// mail. The diagnostics log cannot carry them: it is a failure log, it is capped at 128KB
/// with one backup, and a note per message would evict the failure history it exists for.
///
/// So notes are TALLIES plus a few samples, rendered once into the export.
/// </summary>
public class FieldNotesTests
{
    private static readonly DateTimeOffset When = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FreshNotesAreEmpty()
    {
        var notes = FieldNotes.Fresh;

        Assert.Equal(0, notes.MailWithTextPart);
        Assert.Equal(0, notes.MailWithHtmlOnly);
        Assert.Empty(notes.DetectedEvents);
    }

    // The number that decides how urgent the shared WebView is: how much of the owner's
    // actual mail has no plain-text part at all.
    [Fact]
    public void MailShapeIsTallied()
    {
        var notes = FieldNotes.Fresh
            .WithMail(hasTextPart: true, hasHtmlPart: false, attachments: [])
            .WithMail(hasTextPart: false, hasHtmlPart: true, attachments: [])
            .WithMail(hasTextPart: true, hasHtmlPart: true, attachments: []);

        Assert.Equal(2, notes.MailWithTextPart);
        Assert.Equal(1, notes.MailWithHtmlOnly);
        Assert.Equal(3, notes.MailSeen);
    }

    // Sizes the attachment work: how many, how big, and of what.
    [Fact]
    public void AttachmentsAreTalliedWithTheirLargest()
    {
        var notes = FieldNotes.Fresh
            .WithMail(true, false, [("image/jpeg", 240_000L)])
            .WithMail(true, false, [("application/pdf", 1_200_000L), ("image/png", 30_000L)]);

        Assert.Equal(3, notes.AttachmentsSeen);
        Assert.Equal(1_200_000L, notes.LargestAttachmentBytes);
        Assert.Contains("application/pdf", notes.AttachmentTypes);
        Assert.Contains("image/jpeg", notes.AttachmentTypes);
    }

    // A count alone cannot say whether detection is any GOOD, so a few samples are kept —
    // which is the only way to judge a proposed date without a phone in hand.
    [Fact]
    public void DetectedEventsAreSampled()
    {
        var notes = FieldNotes.Fresh
            .WithDetectedEvent("Recital", new DateOnly(2026, 9, 12), clashed: false, When);

        var sample = Assert.Single(notes.DetectedEvents);
        Assert.Equal("Recital", sample.Title);
        Assert.Equal(new DateOnly(2026, 9, 12), sample.StartsOn);
        Assert.False(sample.Clashed);
    }

    // Post holds no calendar permission, so today every detection is recorded as unchecked
    // — and that must read as unchecked rather than as a clear day, or the notes would say
    // the detection was verified against a calendar nobody read.
    [Fact]
    public void AnUncheckedDayIsNotAClearDay()
    {
        var rendered = FieldNotes.Fresh
            .WithDetectedEvent("Recital", new DateOnly(2026, 9, 12), clashed: null, When)
            .Render();

        Assert.Contains("not checked", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("day was clear", rendered, StringComparison.OrdinalIgnoreCase);
    }

    // Samples are bounded and newest-first. An unbounded list would grow without limit on
    // a phone that syncs every half hour, which is the failure mode the tally exists to
    // avoid in the first place.
    [Fact]
    public void OnlyTheMostRecentSamplesAreKept()
    {
        var notes = FieldNotes.Fresh;
        for (var i = 0; i < FieldNotes.MaxSamples + 5; i++)
        {
            notes = notes.WithDetectedEvent($"Event {i}", new DateOnly(2026, 9, 1), false, When.AddDays(i));
        }

        Assert.Equal(FieldNotes.MaxSamples, notes.DetectedEvents.Count);
        Assert.Equal($"Event {FieldNotes.MaxSamples + 4}", notes.DetectedEvents[0].Title);
    }

    // The count keeps rising even after the samples stop being kept — "how often does this
    // fire" and "is it any good" are different questions.
    [Fact]
    public void TheEventCountOutlivesTheSamples()
    {
        var notes = FieldNotes.Fresh;
        for (var i = 0; i < FieldNotes.MaxSamples + 5; i++)
        {
            notes = notes.WithDetectedEvent($"Event {i}", new DateOnly(2026, 9, 1), false, When);
        }

        Assert.Equal(FieldNotes.MaxSamples + 5, notes.EventsDetected);
    }

    // Sizes the Drive backup: how big the archive gets, and therefore whether plaintext
    // JSON on every change is sane at the owner's real volume.
    [Fact]
    public void TheArchiveHighWaterMarkIsKept()
    {
        var notes = FieldNotes.Fresh
            .WithArchive(ghosts: 40, bytes: 80_000)
            .WithArchive(ghosts: 12, bytes: 20_000);   // after un-filing, it shrank

        Assert.Equal(40, notes.MostGhosts);
        Assert.Equal(80_000, notes.LargestArchiveBytes);
    }

    // Sizes the Rolodex photo pass — the count IS the feature's justification.
    [Fact]
    public void ContactsWithoutPhotosAreCounted()
    {
        var notes = FieldNotes.Fresh.WithContacts(total: 214, withoutPhoto: 138);

        Assert.Equal(214, notes.ContactsTotal);
        Assert.Equal(138, notes.ContactsWithoutPhoto);
    }

    // A later reading replaces an earlier one rather than adding to it: contacts are a
    // snapshot, not a stream, and summing them would report thousands of contacts.
    [Fact]
    public void ContactCountsAreASnapshotNotATally()
    {
        var notes = FieldNotes.Fresh
            .WithContacts(214, 138)
            .WithContacts(215, 137);

        Assert.Equal(215, notes.ContactsTotal);
        Assert.Equal(137, notes.ContactsWithoutPhoto);
    }

    // ---- what the owner actually shares ----

    [Fact]
    public void TheRenderedBlockCarriesEveryNumber()
    {
        var notes = FieldNotes.Fresh
            .WithMail(true, true, [("application/pdf", 900L)])
            .WithMail(false, true, [])
            .WithDetectedEvent("Recital", new DateOnly(2026, 9, 12), true, When)
            .WithArchive(9, 4_096)
            .WithContacts(214, 138);

        var rendered = notes.Render();

        Assert.Contains("Mail seen: 2", rendered, StringComparison.Ordinal);
        Assert.Contains("HTML with no text part: 1", rendered, StringComparison.Ordinal);
        Assert.Contains("application/pdf", rendered, StringComparison.Ordinal);
        Assert.Contains("Recital", rendered, StringComparison.Ordinal);
        Assert.Contains("138", rendered, StringComparison.Ordinal);
    }

    // The export is a file the owner shares deliberately, so it says plainly what is in it
    // rather than smuggling their calendar into a log.
    [Fact]
    public void TheRenderedBlockSaysWhatItContains()
    {
        var rendered = FieldNotes.Fresh
            .WithDetectedEvent("Recital", new DateOnly(2026, 9, 12), false, When)
            .Render();

        Assert.Contains("event titles", rendered, StringComparison.OrdinalIgnoreCase);
    }

    // Nothing observed is not an error, and must not print an empty scaffold either.
    [Fact]
    public void NothingObservedRendersAsNothingObserved()
    {
        Assert.Contains("Nothing observed yet", FieldNotes.Fresh.Render(), StringComparison.Ordinal);
    }

    // ---- persistence ----

    [Fact]
    public void NotesRoundTrip()
    {
        var notes = FieldNotes.Fresh
            .WithMail(true, true, [("image/png", 12L)])
            .WithDetectedEvent("Recital", new DateOnly(2026, 9, 12), true, When)
            .WithArchive(9, 4_096)
            .WithContacts(214, 138);

        var back = FieldNotesCodec.Decode(FieldNotesCodec.Encode(notes));

        Assert.Equal(notes.MailSeen, back.MailSeen);
        Assert.Equal(notes.LargestArchiveBytes, back.LargestArchiveBytes);
        Assert.Equal(notes.ContactsWithoutPhoto, back.ContactsWithoutPhoto);
        Assert.Equal("Recital", back.DetectedEvents.Single().Title);
    }

    // ---- how the built work behaved ----

    // Every one of these is now a working feature rather than a proposal, so the notes
    // record how they FARED, not whether they are worth building. An HTML message the
    // reader could not render, an attachment that would not open, a calendar write that was
    // refused: each is a thing the owner hit and could not otherwise report.
    [Fact]
    public void OutcomesAreTallied()
    {
        var notes = FieldNotes.Fresh
            .WithHtmlRender(rendered: true)
            .WithHtmlRender(rendered: false)
            .WithAttachmentOpened(opened: false)
            .WithCalendarWrite(written: true)
            .WithCalendarWrite(written: false);

        Assert.Equal(1, notes.HtmlRendered);
        Assert.Equal(1, notes.HtmlFellBackToText);
        Assert.Equal(1, notes.AttachmentsRefusedOrFailed);
        Assert.Equal(1, notes.CalendarWritesLanded);
        Assert.Equal(1, notes.CalendarWritesRefused);
    }

    [Fact]
    public void OutcomesAppearInTheRenderedBlock()
    {
        var rendered = FieldNotes.Fresh
            .WithHtmlRender(false)
            .WithCalendarWrite(false)
            .Render();

        Assert.Contains("fell back to text: 1", rendered, StringComparison.Ordinal);
        Assert.Contains("refused: 1", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    public void GarbledNotesDecodeToFresh(string stored)
    {
        Assert.Equal(0, FieldNotesCodec.Decode(stored).MailSeen);
    }
}
