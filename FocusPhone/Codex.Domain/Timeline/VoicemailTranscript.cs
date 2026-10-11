using System.Text.RegularExpressions;

namespace Codex.Domain.Timeline;

/// <summary>
/// Cleans the words of a voicemail out of the text Google wraps them in, for both places a
/// transcript arrives: Fi's "Voicemails by text" SMS and the dialer's voicemail notification.
///
/// Google writes the length of the message as "(0:04)" and follows the transcript with its
/// own line. Neither is what the owner came to read: a duration fragment ("04):") and the
/// "To listen to this message, call …" footer are wrapper, and "No transcript available." is
/// Google saying there are no words at all. Each of those reads as a plain voicemail.
/// </summary>
public static class VoicemailTranscript
{
    /// <summary>Fi's line after every voicemail; a notification carrying it is Fi's wrapper.</summary>
    public const string ListenFooter = "To listen to this message";

    // A leading duration such as "(0:04):" or "0:04:", with the colon after it.
    private static readonly Regex LeadingDuration = new(
        @"^\(?\s*\d{1,2}:\d{2}\s*\)?\s*:?\s*", RegexOptions.CultureInvariant);

    /// <summary>
    /// The transcript in <paramref name="text"/>, or null when there are no words to show.
    /// </summary>
    public static string? Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var body = LeadingDuration.Replace(text.Trim(), "", 1);

        var footer = body.IndexOf(ListenFooter, StringComparison.OrdinalIgnoreCase);
        if (footer >= 0)
        {
            body = body[..footer];
        }

        body = body.Trim();
        if (body.Length == 0 || body.TrimEnd('.').Equals("No transcript available", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return body;
    }
}
