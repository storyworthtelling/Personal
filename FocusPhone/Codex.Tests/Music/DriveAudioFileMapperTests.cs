using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class DriveAudioFileMapperTests
{
    [Fact]
    public void MapAudioFiles_ReadsAudioByMimeType()
    {
        const string json = """
            { "files": [
                { "id": "1", "name": "song.mp3", "mimeType": "audio/mpeg", "size": "5242880",
                  "modifiedTime": "2026-01-02T03:04:05.000Z", "parents": ["folder1"] }
            ] }
            """;

        var file = Assert.Single(DriveAudioFileMapper.MapAudioFiles(json));

        Assert.Equal("1", file.Id);
        Assert.Equal("song.mp3", file.Name);
        Assert.Equal(5242880, file.Size);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), file.ModifiedTime);
        Assert.Equal(["folder1"], file.Parents);
    }

    [Fact]
    public void MapAudioFiles_KeepsAudioByExtensionEvenWhenMimeIsGeneric()
    {
        const string json = """
            { "files": [
                { "id": "2", "name": "track.flac", "mimeType": "application/octet-stream" }
            ] }
            """;

        Assert.Single(DriveAudioFileMapper.MapAudioFiles(json));
    }

    [Fact]
    public void MapAudioFiles_SkipsNonAudioFiles()
    {
        const string json = """
            { "files": [
                { "id": "3", "name": "cover.jpg", "mimeType": "image/jpeg" },
                { "id": "4", "name": "notes.txt", "mimeType": "text/plain" }
            ] }
            """;

        Assert.Empty(DriveAudioFileMapper.MapAudioFiles(json));
    }

    [Fact]
    public void MapAudioFiles_SkipsEntriesMissingIdOrName()
    {
        const string json = """
            { "files": [
                { "name": "orphan.mp3", "mimeType": "audio/mpeg" },
                { "id": "5", "mimeType": "audio/mpeg" }
            ] }
            """;

        Assert.Empty(DriveAudioFileMapper.MapAudioFiles(json));
    }

    [Fact]
    public void MapAudioFiles_ToleratesMissingFilesArray()
    {
        Assert.Empty(DriveAudioFileMapper.MapAudioFiles("{}"));
    }

    [Fact]
    public void MapAudioFiles_DefaultsWhenOptionalFieldsAbsent()
    {
        const string json = """{ "files": [ { "id": "6", "name": "x.m4a" } ] }""";

        var file = Assert.Single(DriveAudioFileMapper.MapAudioFiles(json));

        Assert.Equal(0, file.Size);
        Assert.Empty(file.Parents);
        Assert.Equal(DateTimeOffset.MinValue, file.ModifiedTime);
    }
}
