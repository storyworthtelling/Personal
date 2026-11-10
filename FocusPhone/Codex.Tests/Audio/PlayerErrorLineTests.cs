using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

/// <summary>
/// Audit 08 §13: Score had no player-error listener, so a Drive stream refused with 401, a codec failure or a podcast
/// redirect that went nowhere left no trace. The line names the error, the kind of source and its host, never the path,
/// a file id or a query string.
/// </summary>
public class PlayerErrorLineTests
{
    [Theory]
    [InlineData("https://www.googleapis.com/drive/v3/files/1AbCexample?alt=media", "Drive (www.googleapis.com)")]
    [InlineData("https://feeds.example.com/episodes/42.mp3?token=abc", "podcast (feeds.example.com)")]
    [InlineData("file:///data/user/0/com.example/files/downloads/7.mp3", "download")]
    [InlineData("content://com.example.provider/audio/7", "download")]
    [InlineData(null, "unknown source")]
    public void TheSourceIsItsKindAndHostOnly(string? uri, string source) =>
        Assert.Equal($"PLAYER ERROR_CODE_IO_BAD_HTTP_STATUS on {source}", PlayerErrorLine.Format("ERROR_CODE_IO_BAD_HTTP_STATUS", uri));

    [Fact]
    public void NothingFromThePathOrQueryReachesTheLine()
    {
        var line = PlayerErrorLine.Format("ERROR_CODE_DECODING_FAILED", "https://www.googleapis.com/drive/v3/files/1AbCexample?alt=media&key=secret");

        Assert.DoesNotContain("1AbCexample", line);
        Assert.DoesNotContain("secret", line);
    }
}
