using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class HiddenTrackSplitterTests
{
    private static readonly TimeSpan MinSegment = TimeSpan.FromSeconds(30);

    [Fact]
    public void Plan_ReturnsTheWholeTrackAsOneSegmentWhenThereAreNoGaps()
    {
        var segments = HiddenTrackSplitter.Plan(TimeSpan.FromMinutes(4), [], MinSegment);

        var segment = Assert.Single(segments);
        Assert.Equal(TimeSpan.Zero, segment.Start);
        Assert.Equal(TimeSpan.FromMinutes(4), segment.End);
    }

    // The ordinary hidden-track shape: a real song, a gap, then the bonus track —
    // both sides comfortably longer than a minimum segment.
    [Fact]
    public void Plan_SplitsOnAGapWithARealTrackOnBothSides()
    {
        var gap = new SilenceSpan(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3.25));

        var segments = HiddenTrackSplitter.Plan(TimeSpan.FromMinutes(6), [gap], MinSegment);

        Assert.Equal(2, segments.Count);
        Assert.Equal((TimeSpan.Zero, TimeSpan.FromMinutes(3)), (segments[0].Start, segments[0].End));
        Assert.Equal((TimeSpan.FromMinutes(3.25), TimeSpan.FromMinutes(6)), (segments[1].Start, segments[1].End));
    }

    // Silence right at the start — a rip with a few seconds of dead air before the
    // song even begins — is not a hidden track. There's nothing to split off in front.
    [Fact]
    public void Plan_IgnoresAGapTooCloseToTheStart()
    {
        var gap = new SilenceSpan(TimeSpan.Zero, TimeSpan.FromSeconds(5));

        var segments = HiddenTrackSplitter.Plan(TimeSpan.FromMinutes(4), [gap], MinSegment);

        Assert.Single(segments);
    }

    // A fade-out's trailing silence — nothing plays after it, so it is the track
    // ending, not a boundary to a second song.
    [Fact]
    public void Plan_IgnoresAGapTooCloseToTheEnd()
    {
        var gap = new SilenceSpan(TimeSpan.FromMinutes(3.9), TimeSpan.FromMinutes(4));

        var segments = HiddenTrackSplitter.Plan(TimeSpan.FromMinutes(4), [gap], MinSegment);

        Assert.Single(segments);
    }

    [Fact]
    public void Plan_SplitsOnEveryQualifyingGapAndSkipsTheRest()
    {
        var real = new SilenceSpan(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3.25));
        var tooCloseToEnd = new SilenceSpan(TimeSpan.FromMinutes(5.9), TimeSpan.FromMinutes(6));

        var segments = HiddenTrackSplitter.Plan(TimeSpan.FromMinutes(6), [real, tooCloseToEnd], MinSegment);

        Assert.Equal(2, segments.Count);
        Assert.Equal(TimeSpan.FromMinutes(3.25), segments[1].Start);
        Assert.Equal(TimeSpan.FromMinutes(6), segments[1].End);
    }

    [Fact]
    public void Plan_DoesNotDependOnTheGapsBeingSortedAlready()
    {
        var second = new SilenceSpan(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(4.25));
        var first = new SilenceSpan(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1.25));

        var segments = HiddenTrackSplitter.Plan(TimeSpan.FromMinutes(6), [second, first], MinSegment);

        Assert.Equal(3, segments.Count);
        Assert.Equal(TimeSpan.Zero, segments[0].Start);
        Assert.Equal(TimeSpan.FromMinutes(1), segments[0].End);
    }
}
