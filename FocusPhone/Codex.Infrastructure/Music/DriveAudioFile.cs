namespace Codex.Infrastructure.Music;

/// <summary>An audio file discovered in Google Drive (before its tags are read).</summary>
public record DriveAudioFile(
    string Id,
    string Name,
    string MimeType,
    long Size,
    DateTimeOffset ModifiedTime,
    IReadOnlyList<string> Parents);
