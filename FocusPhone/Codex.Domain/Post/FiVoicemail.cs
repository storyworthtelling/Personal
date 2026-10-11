using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>
/// Google Fi's "Voicemails by text": the transcript arrives as an ordinary SMS from the
/// caller's own number.
///
/// This is what retires the notification scraper. A notification is a one-shot that Post
/// has to be running and listening to catch, and a missed one is gone; a text is a durable
/// record in the SMS provider that Post already reads on every launch. Same information,
/// on a channel that does not depend on timing.
///
/// It has to render AS A VOICEMAIL rather than as a text, or it threads with the wrong
/// half of that person's correspondence — a voicemail belongs beside their calls.
///
/// <para><b>Unverified.</b> Fi's exact wording has not been checked against a real
/// message, so the match is deliberately tolerant and errs toward leaving a message alone:
/// turning a real text into a voicemail is a worse failure than missing a transcript, and
/// harder to notice. Confirming the real shape is an on-device test-plan item.</para>
/// </summary>
public static class FiVoicemail
{
    // The marker has to appear at the START. "Did you get my voicemail?" is a text, and a
    // match anywhere in the body would swallow it.
    private static readonly string[] Openings =
    [
        "voicemail from",
        "new voicemail from",
        "voicemail transcript",
        "voicemail:",
    ];

    // What Fi puts between its wrapper and the transcript itself. A dash only counts when
    // it is SPACED: the wrapper usually names the caller, and "(555) 010-2233" carries a
    // hyphen that arrives before the real separator does. Splitting on the first dash of
    // any kind cuts the message in the middle of the phone number.
    private const char Colon = ':';
    private static readonly string[] SpacedDashes = [" — ", " – ", " - "];

    public static TimelineItem? TryParse(string? fromNumber, string? body, DateTimeOffset when)
    {
        if (string.IsNullOrWhiteSpace(fromNumber) || string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var trimmed = body.TrimStart();
        if (!Openings.Any(opening => trimmed.StartsWith(opening, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var contactKey = ContactKeyNormalizer.NormalizePhone(fromNumber);
        var transcript = Transcript(trimmed);

        return new TimelineItem(
            TimelineItemKind.Voicemail,
            contactKey,
            when,
            transcript,
            Incoming: true,
            // Keyed on the caller, the time and the words: the same message seen twice is
            // one row, and two messages from one caller a minute apart are two.
            SourceId: $"fivm:{contactKey}:{when.ToUnixTimeSeconds()}:{transcript.GetHashCode():X8}");
    }

    private static string Transcript(string body)
    {
        var cut = WrapperSeparator(body);
        if (cut < 0)
        {
            foreach (var dash in SpacedDashes)
            {
                var at = body.IndexOf(dash, StringComparison.Ordinal);
                if (at >= 0)
                {
                    cut = at + dash.Length;
                    break;
                }
            }
        }

        // A voicemail Fi could not transcribe still deserves a row: the owner needs to know
        // someone left a message, even without the words.
        if (cut < 0 || cut >= body.Length)
        {
            return "Voicemail";
        }

        return VoicemailTranscript.Extract(body[cut..]) ?? "Voicemail";
    }

    // The first colon that is not inside a time. Fi writes the length as "(0:04)", and that
    // colon is not the wrapper's: cutting there leaves "04): …" as the transcript.
    private static int WrapperSeparator(string body)
    {
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != Colon)
            {
                continue;
            }
            var insideTime = i > 0 && char.IsAsciiDigit(body[i - 1])
                && i + 1 < body.Length && char.IsAsciiDigit(body[i + 1]);
            if (!insideTime)
            {
                return i + 1;
            }
        }
        return -1;
    }
}
