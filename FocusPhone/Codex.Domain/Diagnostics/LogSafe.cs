namespace Codex.Domain.Diagnostics;

/// <summary>
/// What a log line may say about a value the owner's correspondence carries. The durable log is
/// uploaded by the log loop, so a number is its last two digits and a link is its scheme and host:
/// enough to tell two failures apart, not enough to dial or follow.
/// </summary>
public static class LogSafe
{
    public static string Number(string? number)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 2 ? $"…{digits[^2..]}" : "a number";
    }

    public static string Link(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "an unreadable link";
        }

        // mailto: and tel: carry the address itself where a web link has its host, so they say only what they are.
        var hasAuthority = url!.Contains("://", StringComparison.Ordinal) && !string.IsNullOrEmpty(uri.Host);
        return hasAuthority ? $"{uri.Scheme} link to {uri.Host}" : $"{uri.Scheme} link";
    }
}
