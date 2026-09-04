using System.Globalization;
using System.Text;

namespace Codex.Domain.Post;

/// <summary>
/// One date a message proposed, kept so the detection can be judged rather than counted.
/// <paramref name="Clashed"/> is nullable because the day cannot always be read — no
/// calendar permission, no calendar, a provider that refused — and "not checked" is not the
/// same answer as "clear".
/// </summary>
public sealed record DetectedEventSample(string Title, DateOnly StartsOn, bool? Clashed, DateTimeOffset SeenUtc);

/// <summary>
/// What Post noticed while running, as opposed to what went wrong.
///
/// The diagnostics log cannot carry this. It is a <b>failure</b> log — WARN, ERROR, CRASH,
/// every call site an exception path — and it is capped at 128KB with a single backup, so
/// a note per message would evict the failure history it exists for within one sync. Field
/// notes are therefore <b>tallies</b> plus a few bounded samples, rendered once into the
/// export the owner shares.
///
/// Two kinds of thing are recorded, and the distinction matters. The <b>shape</b> of the
/// owner's real world — how much of their mail is HTML with no text part, what their
/// attachments look like, how far the archive grows, how many contacts have no face — and
/// the <b>outcomes</b> of the surfaces built on top of it: did the reader render, did the
/// attachment open, did the calendar accept the write. The first sizes the problem; the
/// second says whether the answer works on their phone rather than on the fakes.
///
/// Detected dates are the one case a count cannot settle, so a few are kept in full: only
/// the words can say whether a proposal was worth making.
/// </summary>
public sealed record FieldNotes(
    int MailSeen,
    int MailWithTextPart,
    int MailWithHtmlOnly,
    int AttachmentsSeen,
    long LargestAttachmentBytes,
    IReadOnlyList<string> AttachmentTypes,
    int EventsDetected,
    IReadOnlyList<DetectedEventSample> DetectedEvents,
    int MostGhosts,
    long LargestArchiveBytes,
    int ContactsTotal,
    int ContactsWithoutPhoto,
    int HtmlRendered = 0,
    int HtmlFellBackToText = 0,
    int AttachmentsOpened = 0,
    int AttachmentsRefusedOrFailed = 0,
    int CalendarWritesLanded = 0,
    int CalendarWritesRefused = 0)
{
    /// <summary>
    /// How many detected events are kept in full. Bounded because a phone syncing every
    /// half hour would otherwise grow this without limit — the exact failure the tally
    /// exists to avoid.
    /// </summary>
    public const int MaxSamples = 8;

    /// <summary>How many distinct attachment types are worth naming before it is just noise.</summary>
    private const int MaxTypes = 8;

    public static FieldNotes Fresh { get; } =
        new(0, 0, 0, 0, 0, [], 0, [], 0, 0, 0, 0);

    public FieldNotes WithMail(
        bool hasTextPart, bool hasHtmlPart, IReadOnlyList<(string MimeType, long Bytes)> attachments)
    {
        var types = AttachmentTypes.ToList();
        foreach (var (mimeType, _) in attachments)
        {
            if (!types.Contains(mimeType, StringComparer.OrdinalIgnoreCase) && types.Count < MaxTypes)
            {
                types.Add(mimeType);
            }
        }

        var largest = attachments.Count == 0
            ? LargestAttachmentBytes
            : Math.Max(LargestAttachmentBytes, attachments.Max(attachment => attachment.Bytes));

        return this with
        {
            MailSeen = MailSeen + 1,
            MailWithTextPart = MailWithTextPart + (hasTextPart ? 1 : 0),
            MailWithHtmlOnly = MailWithHtmlOnly + (!hasTextPart && hasHtmlPart ? 1 : 0),
            AttachmentsSeen = AttachmentsSeen + attachments.Count,
            LargestAttachmentBytes = largest,
            AttachmentTypes = types,
        };
    }

    public FieldNotes WithDetectedEvent(string title, DateOnly startsOn, bool? clashed, DateTimeOffset seenUtc)
    {
        var samples = new List<DetectedEventSample>(DetectedEvents.Count + 1)
        {
            new(title, startsOn, clashed, seenUtc),
        };
        samples.AddRange(DetectedEvents.Take(MaxSamples - 1));

        // The count keeps climbing after the samples stop: "how often does this fire" and
        // "is it any good" are different questions and only one of them needs the words.
        return this with { EventsDetected = EventsDetected + 1, DetectedEvents = samples };
    }

    /// <summary>
    /// Whether the mail reader rendered a message's HTML or fell back to the extracted
    /// text. The fallback is not a failure — a message still arrived readable — but it is
    /// the number that says whether the reader is doing its job on real mail.
    /// </summary>
    public FieldNotes WithHtmlRender(bool rendered) => this with
    {
        HtmlRendered = HtmlRendered + (rendered ? 1 : 0),
        HtmlFellBackToText = HtmlFellBackToText + (rendered ? 0 : 1),
    };

    /// <summary>Whether an attachment opened. Refusals count here too — a refusal is a design decision meeting real mail.</summary>
    public FieldNotes WithAttachmentOpened(bool opened) => this with
    {
        AttachmentsOpened = AttachmentsOpened + (opened ? 1 : 0),
        AttachmentsRefusedOrFailed = AttachmentsRefusedOrFailed + (opened ? 0 : 1),
    };

    /// <summary>Whether a confirmed date reached the calendar. A refusal is the one the owner would notice.</summary>
    public FieldNotes WithCalendarWrite(bool written) => this with
    {
        CalendarWritesLanded = CalendarWritesLanded + (written ? 1 : 0),
        CalendarWritesRefused = CalendarWritesRefused + (written ? 0 : 1),
    };

    /// <summary>The archive's high-water mark — it shrinks when things are un-filed.</summary>
    public FieldNotes WithArchive(int ghosts, long bytes) => this with
    {
        MostGhosts = Math.Max(MostGhosts, ghosts),
        LargestArchiveBytes = Math.Max(LargestArchiveBytes, bytes),
    };

    /// <summary>
    /// A snapshot, replacing whatever was read before. Contacts are a state, not a stream;
    /// summing every reading would report thousands of contacts by the end of a week.
    /// </summary>
    public FieldNotes WithContacts(int total, int withoutPhoto) => this with
    {
        ContactsTotal = total,
        ContactsWithoutPhoto = withoutPhoto,
    };

    /// <summary>
    /// The block that goes into the shared diagnostics export. It states what it contains,
    /// because the export is a file the owner hands over deliberately and their calendar
    /// should not be smuggled into a log.
    /// </summary>
    public string Render()
    {
        if (MailSeen == 0 && EventsDetected == 0 && ContactsTotal == 0 && MostGhosts == 0
            && HtmlRendered == 0 && HtmlFellBackToText == 0 && AttachmentsOpened == 0
            && AttachmentsRefusedOrFailed == 0 && CalendarWritesLanded == 0 && CalendarWritesRefused == 0)
        {
            return "FIELD NOTES\nNothing observed yet.\n";
        }

        var text = new StringBuilder();
        text.AppendLine("FIELD NOTES");
        text.AppendLine("What Post noticed while running — counts, not failures. Includes a few detected");
        text.AppendLine("event titles so the date detection can be judged rather than only counted.");
        text.AppendLine();

        text.AppendLine(Invariant($"Mail seen: {MailSeen}"));
        text.AppendLine(Invariant($"  with a plain-text part: {MailWithTextPart}"));
        text.AppendLine(Invariant($"  HTML with no text part: {MailWithHtmlOnly}"));
        text.AppendLine(Invariant($"Attachments seen: {AttachmentsSeen}, largest {LargestAttachmentBytes} bytes"));
        if (AttachmentTypes.Count > 0)
        {
            text.AppendLine(Invariant($"  types: {string.Join(", ", AttachmentTypes)}"));
        }

        text.AppendLine(Invariant($"Dates detected: {EventsDetected}"));
        foreach (var sample in DetectedEvents)
        {
            var clash = sample.Clashed switch
            {
                true => " (clashed)",
                false => " (day was clear)",
                null => " (day not checked — Post cannot read the calendar yet)",
            };
            text.AppendLine(Invariant($"  {sample.StartsOn:yyyy-MM-dd}  {sample.Title}{clash}"));
        }

        text.AppendLine(Invariant($"Archive high-water mark: {MostGhosts} ghosts, {LargestArchiveBytes} bytes"));
        text.AppendLine(Invariant($"Contacts: {ContactsTotal}, without a photo: {ContactsWithoutPhoto}"));

        text.AppendLine();
        text.AppendLine("How the newer surfaces fared:");
        text.AppendLine(Invariant($"  HTML mail rendered: {HtmlRendered}, fell back to text: {HtmlFellBackToText}"));
        text.AppendLine(Invariant($"  Attachments opened: {AttachmentsOpened}, refused or failed: {AttachmentsRefusedOrFailed}"));
        text.AppendLine(Invariant($"  Calendar writes landed: {CalendarWritesLanded}, refused: {CalendarWritesRefused}"));
        return text.ToString();
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
