using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class TrackTests
{
    private static Track Plain(string id) =>
        new(id, "Title", "Artist", "Album", "Artist", 1, 1, "Rock", TimeSpan.FromMinutes(3));

    [Fact]
    public void ResolvedSourceId_IsItsOwnIdWhenItIsNotASplitSegment()
    {
        Assert.Equal("abc", Plain("abc").ResolvedSourceId);
    }

    // A split segment's own Id is what the queue and progress book key on (each half
    // needs its own identity), but playback has to resolve the file that is actually
    // shared between both halves.
    [Fact]
    public void ResolvedSourceId_IsTheSourceFileWhenItIsASplitSegment()
    {
        var segment = Plain("abc#1") with { SourceFileId = "abc" };

        Assert.Equal("abc", segment.ResolvedSourceId);
    }
}
