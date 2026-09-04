namespace Codex.Infrastructure.Music;

/// <summary>
/// Decides whether a file is playable audio — by MIME type, else by extension
/// (Drive sometimes reports FLAC and others as application/octet-stream). The
/// single source of truth shared by the Drive listing and the JSON mapper.
/// </summary>
public static class AudioFileTypes
{
    /// <summary>
    /// The playable audio extensions, dot-prefixed and lowercase — the single list the
    /// Drive/tag pipeline classifies by and the music "how to add" guide shows the user.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedExtensions =
    [
        ".mp3", ".m4a", ".m4b", ".aac", ".flac", ".ogg", ".oga", ".opus", ".wav", ".alac", ".wma",
    ];

    private static readonly HashSet<string> Extensions =
        new(SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    public static bool IsAudio(string name, string? mimeType) =>
        (mimeType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ?? false)
        || Extensions.Contains(Path.GetExtension(name));
}
