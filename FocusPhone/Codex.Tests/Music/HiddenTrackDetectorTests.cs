using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class HiddenTrackDetectorTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(100);

    private static List<float> Levels(params (float Level, int Count)[] runs)
    {
        var levels = new List<float>();
        foreach (var (level, count) in runs)
        {
            levels.AddRange(Enumerable.Repeat(level, count));
        }
        return levels;
    }

    [Fact]
    public void FindGaps_FindsOneLongQuietRunInTheMiddle()
    {
        // 2s loud, 3s silent, 2s loud — a hidden track's gap.
        var levels = Levels((0.8f, 20), (0.01f, 30), (0.8f, 20));

        var gaps = HiddenTrackDetector.FindGaps(
            levels, Frame, silenceThreshold: 0.02f, minGap: TimeSpan.FromSeconds(2));

        var gap = Assert.Single(gaps);
        Assert.Equal(TimeSpan.FromSeconds(2), gap.Start);
        Assert.Equal(TimeSpan.FromSeconds(5), gap.End);
        Assert.Equal(TimeSpan.FromSeconds(3), gap.Duration);
    }

    // A held quiet passage inside a song — a breakdown, an intro — is not a hidden-track
    // boundary. Only a run at least minGap long counts.
    [Fact]
    public void FindGaps_IgnoresAQuietRunShorterThanTheMinimum()
    {
        var levels = Levels((0.8f, 20), (0.01f, 5), (0.8f, 20));

        var gaps = HiddenTrackDetector.FindGaps(
            levels, Frame, silenceThreshold: 0.02f, minGap: TimeSpan.FromSeconds(2));

        Assert.Empty(gaps);
    }

    [Fact]
    public void FindGaps_FindsEveryQualifyingRunInOrder()
    {
        var levels = Levels((0.8f, 10), (0.01f, 25), (0.8f, 10), (0.01f, 30), (0.8f, 10));

        var gaps = HiddenTrackDetector.FindGaps(
            levels, Frame, silenceThreshold: 0.02f, minGap: TimeSpan.FromSeconds(2));

        Assert.Equal(2, gaps.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), gaps[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(3.5), gaps[0].End);
        Assert.Equal(TimeSpan.FromSeconds(4.5), gaps[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(7.5), gaps[1].End);
    }

    // A track that trails off silent at the very end (no audio after it) still reports
    // the gap — HiddenTrackDetector only measures loudness, not what's a boundary
    // worth splitting on. That distinction is HiddenTrackSplitter's job.
    [Fact]
    public void FindGaps_ReportsATrailingSilentRunThatNeverGetsLoudAgain()
    {
        var levels = Levels((0.8f, 20), (0.01f, 30));

        var gaps = HiddenTrackDetector.FindGaps(
            levels, Frame, silenceThreshold: 0.02f, minGap: TimeSpan.FromSeconds(2));

        var gap = Assert.Single(gaps);
        Assert.Equal(TimeSpan.FromSeconds(2), gap.Start);
        Assert.Equal(TimeSpan.FromSeconds(5), gap.End);
    }

    [Fact]
    public void FindGaps_ReturnsNothingForATrackWithNoSilence()
    {
        var levels = Levels((0.5f, 50));

        var gaps = HiddenTrackDetector.FindGaps(
            levels, Frame, silenceThreshold: 0.02f, minGap: TimeSpan.FromSeconds(2));

        Assert.Empty(gaps);
    }

    [Fact]
    public void FindGaps_ReturnsNothingForAnEmptyTrack()
    {
        var gaps = HiddenTrackDetector.FindGaps(
            [], Frame, silenceThreshold: 0.02f, minGap: TimeSpan.FromSeconds(2));

        Assert.Empty(gaps);
    }

    // A level exactly at the threshold counts as silent — the boundary is inclusive,
    // so a track authored right at the threshold doesn't slip through by a hair.
    [Fact]
    public void FindGaps_TreatsExactlyTheThresholdAsSilent()
    {
        var levels = Levels((0.8f, 20), (0.02f, 30), (0.8f, 20));

        var gaps = HiddenTrackDetector.FindGaps(
            levels, Frame, silenceThreshold: 0.02f, minGap: TimeSpan.FromSeconds(2));

        Assert.Single(gaps);
    }
}
