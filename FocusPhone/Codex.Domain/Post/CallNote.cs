using System.Text.RegularExpressions;
using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>
/// The owner's own note on a call.
///
/// The design interview asked for a summary of the conversation, which is not deliverable:
/// third-party call recording is closed on Android — the microphone route went in 10, the
/// accessibility route in May 2022 — and Pixel's own dialer is a first-party exception Post
/// cannot join. So the note is written by the person who was on the call.
///
/// Google's Call Notes can fill it in where it exists, but it exports only through the
/// share sheet, so Post registers as a share target and takes what arrives. It does not
/// work on the 8a at all, which is why manual entry is the default path rather than the
/// fallback — the share is the bonus, not the plan.
/// </summary>
public static partial class CallNote
{
    // Enough to find a number in free text: a leading +, digits, and the separators people
    // and machines put between them.
    [GeneratedRegex(@"\+?\d[\d\s().\-]{6,}\d")]
    private static partial Regex PhoneLike();

    public static TimelineItem For(string number, string text, DateTimeOffset when) =>
        new(
            TimelineItemKind.Call,
            ContactKeyNormalizer.NormalizePhone(number),
            when,
            text,
            // The owner wrote it, so it is theirs. Marking it incoming would put it on the
            // wrong side of the conversation.
            Incoming: false,
            SourceId: $"note:{when.ToUnixTimeMilliseconds()}");

    /// <summary>
    /// Reads a note shared in from elsewhere. A share carries no structure at all, so the
    /// number has to be found in the text — and without one the note has no thread to join,
    /// which is a reason to ask rather than to guess whose call it was.
    /// </summary>
    public static TimelineItem? FromShare(string? shared, DateTimeOffset when)
    {
        if (string.IsNullOrWhiteSpace(shared))
        {
            return null;
        }

        var match = PhoneLike().Match(shared);
        if (!match.Success)
        {
            return null;
        }

        var text = shared.Remove(match.Index, match.Length)
            .Replace("Call with", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .TrimStart(':', '-', '–', '—')
            .Trim();

        return For(match.Value, text, when);
    }
}
