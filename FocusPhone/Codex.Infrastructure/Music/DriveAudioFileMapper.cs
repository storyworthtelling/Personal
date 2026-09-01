using System.Text.Json;

namespace Codex.Infrastructure.Music;

/// <summary>An audio file discovered in Google Drive (before its tags are read).</summary>
public record DriveAudioFile(
    string Id,
    string Name,
    string MimeType,
    long Size,
    DateTimeOffset ModifiedTime,
    IReadOnlyList<string> Parents);

/// <summary>
/// Maps a Google Drive <c>files.list</c> JSON response into audio-file records,
/// keeping only entries that look like audio (by MIME type or extension — Drive
/// sometimes reports FLAC/others as application/octet-stream). Pure JSON→model, so
/// it is unit-tested with fixture payloads and needs no Google SDK.
/// </summary>
public static class DriveAudioFileMapper
{
    public static IReadOnlyList<DriveAudioFile> MapAudioFiles(string filesListJson)
    {
        using var document = JsonDocument.Parse(filesListJson);
        if (!document.RootElement.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<DriveAudioFile>();
        foreach (var file in files.EnumerateArray())
        {
            var id = Text(file, "id");
            var name = Text(file, "name");
            if (id is null || name is null)
            {
                continue;
            }

            var mimeType = Text(file, "mimeType") ?? "application/octet-stream";
            if (!AudioFileTypes.IsAudio(name, mimeType))
            {
                continue;
            }

            result.Add(new DriveAudioFile(
                id,
                name,
                mimeType,
                ParseSize(Text(file, "size")),
                ParseModifiedTime(Text(file, "modifiedTime")),
                ParseParents(file)));
        }

        return result;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long ParseSize(string? size) =>
        long.TryParse(size, out var parsed) ? parsed : 0;

    private static DateTimeOffset ParseModifiedTime(string? modifiedTime) =>
        DateTimeOffset.TryParse(modifiedTime, out var parsed) ? parsed.ToUniversalTime() : DateTimeOffset.MinValue;

    private static IReadOnlyList<string> ParseParents(JsonElement file)
    {
        if (!file.TryGetProperty("parents", out var parents) || parents.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. parents.EnumerateArray()
            .Where(parent => parent.ValueKind == JsonValueKind.String)
            .Select(parent => parent.GetString()!)];
    }
}
