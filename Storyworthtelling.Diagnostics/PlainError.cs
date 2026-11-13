using System.Text.RegularExpressions;

namespace Storyworthtelling.Diagnostics;

/// <summary>
/// The one thing a person can act on, in words, from a recorded error (audit 11 §22). An exception message is written
/// for a diagnostics file — the call that failed, the exception type, the inner chain — and every app used to put it on
/// screen as it was. This reads it for the causes the suite actually meets and falls back to the error's own words,
/// without the call and type in front, for the rest. Folio's desk had this alone; it is shared so every app says the same.
/// </summary>
public static partial class PlainError
{
    private const int LongestUnknown = 120;

    /// <param name="recorded">The recorded error, often an exception's message or the chain joined with " ← ".</param>
    /// <param name="service">Who a refusal or a rate limit came from ("Claude", "Google Drive"), or null.</param>
    public static string Describe(string? recorded, string? service = null)
    {
        if (string.IsNullOrWhiteSpace(recorded))
        {
            return string.Empty;
        }

        // Checked in order: the first that matches wins. Credit comes before the generic HTTP failure it arrives as,
        // truncation before "connection" because a truncated reply mentions neither, and storage and memory before
        // anything a failed write might also say.
        foreach (var (markers, words) in Known(service))
        {
            if (markers.Any(marker => recorded.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return words;
            }
        }

        return Shorten(WithoutPrefixes(recorded));
    }

    /// <summary>The same, over an exception's whole chain (its types and messages), since the cause is often inner.</summary>
    public static string Describe(Exception exception, string? service = null)
    {
        var links = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            links.Add($"{current.GetType().Name}: {current.Message}");
        }
        return Describe(string.Join(" ← ", links), service);
    }

    private static (string[] Markers, string Words)[] Known(string? service) =>
    [
        (["credit balance"], "your Anthropic credit balance is too low"),
        (["truncated", "max_tokens"], "the reply was cut off at the output limit"),
        (["OutOfMemory", "Failed to allocate", "Java heap space", "until OOM"], "the phone ran out of memory"),
        (["ENOSPC", "No space left", "not enough space", "disk full"], "the phone is out of storage space"),
        (["Unable to resolve host", "No address associated", "Network is unreachable", "EAI_NODATA", "UnknownHostException"],
            "the phone has no network"),
        (["401", "403", "Unauthorized", "Forbidden", "invalid x-api-key", "authentication_error", "permission_error"],
            service switch
            {
                null => "the request was turned away",
                "Claude" => "the Claude key was rejected",
                _ => $"{service} refused the request",
            }),
        (["rate limit", "rateLimitExceeded", "429", "overloaded"],
            service is null ? "the service is rate-limiting requests" : $"{service} is rate-limiting requests"),
        (["timed out", "Timeout", "TimeoutException"], "the request timed out"),
        (["connection abort", "Connection reset", "Connection failure", "Socket closed", "Broken pipe"],
            "the connection dropped part-way through"),
    ];

    // "Call: ExceptionType: message ← Inner: message" -> "message". The call and the type are in the diagnostics file;
    // on screen they are noise in front of the only part that explains anything.
    private static string WithoutPrefixes(string recorded)
    {
        var chain = recorded.IndexOf(" ← ", StringComparison.Ordinal);
        var text = chain >= 0 ? recorded[..chain] : recorded;
        return Prefixes().Replace(text, string.Empty).Trim();
    }

    [GeneratedRegex(@"^(?:[^:]{1,60}: )?(?:[A-Za-z.]+Exception: )?")]
    private static partial Regex Prefixes();

    private static string Shorten(string text) =>
        text.Length <= LongestUnknown ? text : text[..(LongestUnknown - 1)].TrimEnd() + "…";
}
