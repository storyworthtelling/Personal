using System.Text.RegularExpressions;

namespace Folio.Domain.Pipeline;

// The recorded error is written for a diagnostics file: the call that failed, the exception type, the
// inner chain. The desk needs the one thing a person can act on, in words, so this reads the recorded
// text for the causes Folio actually meets and falls back to the error's own words for the rest.
public static partial class PlainError
{
    private const int LongestUnknown = 120;

    // Checked in order: the first that matches wins. Credit comes before the generic HTTP failure it
    // arrives as, and truncation before "connection" because a truncated reply mentions neither.
    private static readonly (string[] Markers, string Words)[] Known =
    [
        (["credit balance"], "your Anthropic credit balance is too low"),
        (["truncated", "max_tokens"], "the reply was cut off at the output limit"),
        (["Unable to resolve host", "No address associated", "Network is unreachable", "EAI_NODATA"],
            "the phone has no network"),
        (["401", "403", "invalid x-api-key", "authentication_error", "permission_error"],
            "the Claude key was rejected"),
        (["rate limit", "429", "overloaded"], "Anthropic is rate-limiting requests"),
        (["connection abort", "Connection reset", "Connection failure", "Socket closed", "Broken pipe"],
            "the connection dropped part-way through the reply"),
    ];

    public static string Describe(string? recorded)
    {
        if (string.IsNullOrWhiteSpace(recorded))
            return string.Empty;

        foreach (var (markers, words) in Known)
        {
            if (markers.Any(marker => recorded.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                return words;
        }

        return Shorten(WithoutPrefixes(recorded));
    }

    // "Call: ExceptionType: message ← Inner: message" -> "message". The call and the type are in the
    // diagnostics file; on the desk they are noise in front of the only part that explains anything.
    private static string WithoutPrefixes(string recorded)
    {
        var chain = recorded.IndexOf(" ← ", StringComparison.Ordinal);
        var text = chain >= 0 ? recorded[..chain] : recorded;
        return Prefixes().Replace(text, string.Empty).Trim();
    }

    [GeneratedRegex(@"^(?:[^:]{1,60}: )?(?:[A-Za-z]+Exception: )?")]
    private static partial Regex Prefixes();

    private static string Shorten(string text) =>
        text.Length <= LongestUnknown ? text : text[..(LongestUnknown - 1)].TrimEnd() + "…";
}
