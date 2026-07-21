using FocusPhone.Infrastructure.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class AudioFileTypesTests
{
    [Theory]
    [InlineData("song.mp3", "audio/mpeg", true)]
    [InlineData("song.flac", "application/octet-stream", true)]
    [InlineData("song.m4a", null, true)]
    [InlineData("song.OPUS", "application/octet-stream", true)]
    [InlineData("cover.jpg", "image/jpeg", false)]
    [InlineData("notes.txt", "text/plain", false)]
    [InlineData("clip", "audio/x-wav", true)]
    [InlineData("noext", null, false)]
    public void IsAudio_ClassifiesByMimeOrExtension(string name, string? mimeType, bool expected)
    {
        Assert.Equal(expected, AudioFileTypes.IsAudio(name, mimeType));
    }
}
