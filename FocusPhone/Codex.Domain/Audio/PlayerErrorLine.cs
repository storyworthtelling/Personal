namespace Codex.Domain.Audio;

/// <summary>
/// The log line for a playback error (audit 08 §13): the player's error code name, the kind of source and its host.
/// Never the path, a Drive file id or a query string, which can carry a token.
/// </summary>
public static class PlayerErrorLine
{
    public static string Format(string errorCodeName, string? uri) => $"PLAYER {errorCodeName} on {Source(uri)}";

    private static string Source(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return "unknown source";
        }
        if (parsed.Scheme is "file" or "content")
        {
            return "download";
        }
        var host = parsed.Host;
        var kind = host.EndsWith("googleapis.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("googleusercontent.com", StringComparison.OrdinalIgnoreCase) ? "Drive"
            : host.Contains("tidal", StringComparison.OrdinalIgnoreCase) ? "TIDAL"
            : "podcast";
        return $"{kind} ({host})";
    }
}
