namespace FocusPhone.Infrastructure.Music;

/// <summary>
/// Decides whether a file is playable audio — by MIME type, else by extension
/// (Drive sometimes reports FLAC and others as application/octet-stream). The
/// single source of truth shared by the Drive listing and the JSON mapper.
/// </summary>
public static class AudioFileTypes
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".m4b", ".aac", ".flac", ".ogg", ".oga", ".opus", ".wav", ".alac", ".wma",
    };

    public static bool IsAudio(string name, string? mimeType) =>
        (mimeType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ?? false)
        || Extensions.Contains(Path.GetExtension(name));
}
