using System.Globalization;
using System.Text;

namespace Codex.Domain.Post;

/// <summary>
/// One date a message proposed, kept so the detection can be judged rather than counted.
/// <paramref name="Clashed"/> is nullable because Post cannot read the calendar yet — it
/// holds no calendar permission — and "not checked" is not the same answer as "clear".
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
/// Everything here answers a question the deferred work cannot be designed without:
/// how much of the owner's real mail is HTML with no text part (does the shared web view
/// matter?), what their attachments actually look like, how large the ghost archive grows
/// (is plaintext JSON on every change sane?), how many contacts have no photo (is the
/// Rolodex pass worth building?), and whether date detection produces anything worth
/// proposing — which a count alone cannot answer, so a few are kept in full.
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
    int ContactsWithoutPhoto)
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
        if (MailSeen == 0 && EventsDetected == 0 && ContactsTotal == 0 && MostGhosts == 0)
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
        return text.ToString();
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
