using Watch.Domain.Kit;

namespace Watch.Tests.Kit;

public class WaveGeometryTests
{
    private static readonly WaveFrame Frame = new(20, 160, 90);

    [Fact]
    public void SpansTheFrame()
    {
        var points = WaveGeometry.Points(Frame, 30, paused: false);

        Assert.Equal(WaveGeometry.Segments + 1, points.Count);
        Assert.Equal(20, points[0].X, 9);
        Assert.Equal(180, points[^1].X, 9);
    }

    [Fact]
    public void TwoPeriods()
    {
        var points = WaveGeometry.Points(Frame, 30, paused: false);
        var crests = Enumerable.Range(1, points.Count - 2)
            .Count(i => points[i].Y < points[i - 1].Y && points[i].Y <= points[i + 1].Y);

        Assert.Equal(WaveGeometry.Periods, crests);
    }

    [Fact]
    public void HeightIsTheAmplitude()
    {
        var points = WaveGeometry.Points(Frame, 30, paused: false);

        Assert.Equal(90 - 30, points.Min(p => p.Y), 1);
        Assert.Equal(90 + 30, points.Max(p => p.Y), 1);
    }

    [Fact]
    public void FirstCrestRisesUp()
    {
        var points = WaveGeometry.Points(Frame, 30, paused: false);

        Assert.True(points[WaveGeometry.Segments / 8].Y < Frame.Baseline);
    }

    [Fact]
    public void FlatWhenPaused()
    {
        var points = WaveGeometry.Points(Frame, 30, paused: true);

        Assert.All(points, p => Assert.Equal(90, p.Y, 9));
    }

    [Fact]
    public void BrightFollowsProgress()
    {
        var all = WaveGeometry.Points(Frame, 30, paused: false);
        var bright = WaveGeometry.Bright(Frame, 30, paused: false, progress: 0.38);

        Assert.Equal((int)Math.Round(WaveGeometry.Segments * 0.38) + 1, bright.Count);
        Assert.Equal(all.Take(bright.Count), bright);
    }

    [Fact]
    public void NothingBrightAtTheStart() =>
        Assert.Empty(WaveGeometry.Bright(Frame, 30, paused: false, progress: 0));

    [Fact]
    public void AllBrightAtTheEnd() =>
        Assert.Equal(WaveGeometry.Segments + 1, WaveGeometry.Bright(Frame, 30, paused: false, progress: 1.4).Count);

    [Fact]
    public void BrightStaysWhenPaused()
    {
        var bright = WaveGeometry.Bright(Frame, 30, paused: true, progress: 0.38);

        Assert.Equal((int)Math.Round(WaveGeometry.Segments * 0.38) + 1, bright.Count);
        Assert.All(bright, p => Assert.Equal(90, p.Y, 9));
    }

    [Theory]
    [InlineData(0, WaveGeometry.Quietest)]
    [InlineData(1, WaveGeometry.Loudest)]
    [InlineData(-3, WaveGeometry.Quietest)]
    [InlineData(7, WaveGeometry.Loudest)]
    public void VolumeIsHeight(double volume, double expected) =>
        Assert.Equal(expected, WaveGeometry.AmplitudeFor(volume), 9);

    [Fact]
    public void LouderIsTaller() =>
        Assert.True(WaveGeometry.AmplitudeFor(0.7) > WaveGeometry.AmplitudeFor(0.3));

    [Fact]
    public void CrownNudgesByStep()
    {
        Assert.Equal(0.20 + 2 * WaveGeometry.Step, WaveGeometry.Nudge(0.20, 2), 9);
        Assert.Equal(0.20 - WaveGeometry.Step, WaveGeometry.Nudge(0.20, -1), 9);
    }

    [Fact]
    public void CrownStopsAtTheEnds()
    {
        Assert.Equal(WaveGeometry.Loudest, WaveGeometry.Nudge(0.34, 10), 9);
        Assert.Equal(WaveGeometry.Quietest, WaveGeometry.Nudge(0.06, -10), 9);
    }

    [Fact]
    public void QuietestStillWaves() => Assert.True(WaveGeometry.Quietest > 0);

    [Theory]
    [InlineData(5, WaveTarget.Back)]
    [InlineData(200, WaveTarget.PlayPause)]
    [InlineData(395, WaveTarget.Skip)]
    public void EdgesGoBackAndSkip(double x, WaveTarget expected) =>
        Assert.Equal(expected, WaveGeometry.TargetAt(x, 400, 48));

    [Fact]
    public void EdgesAreAtLeastATouchTarget()
    {
        Assert.Equal(WaveTarget.Back, WaveGeometry.TargetAt(47, 120, 48));
        Assert.Equal(WaveTarget.Skip, WaveGeometry.TargetAt(73, 120, 48));
    }

    [Fact]
    public void MiddleAlwaysRemains() =>
        Assert.Equal(WaveTarget.PlayPause, WaveGeometry.TargetAt(50, 100, 48));
}
