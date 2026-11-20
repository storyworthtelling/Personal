using Watch.Domain.Design;

namespace Watch.Tests.Design;

public class WatchTokensTests
{
    [Theory]
    [InlineData(WatchApp.Point, 0.62, 0.19, 300, 0.82, 0.10, 170)]
    [InlineData(WatchApp.Dash, 0.30, 0.10, 262, 0.32, 0.12, 15)]
    [InlineData(WatchApp.Wave, 0.62, 0.03, 250, 0.80, 0.16, 75)]
    [InlineData(WatchApp.Beat, 0.38, 0.09, 150, 0.63, 0.24, 27)]
    public void EachAppHasItsEnds(WatchApp app, double nightL, double nightC, double nightH, double dayL, double dayC, double dayH)
    {
        var range = WatchTokens.RangeOf(app);

        Assert.Equal(new Oklch(nightL, nightC, nightH), range.Night);
        Assert.Equal(new Oklch(dayL, dayC, dayH), range.Day);
    }

    [Fact]
    public void EveryAppHasARange()
    {
        foreach (var app in Enum.GetValues<WatchApp>())
        {
            Assert.NotEqual(default, WatchTokens.RangeOf(app).Night);
        }
        Assert.Equal(4, Enum.GetValues<WatchApp>().Length);
    }

    [Fact]
    public void ThreeTypeSizesDescending()
    {
        Assert.True(WatchTokens.Type.Glance > WatchTokens.Type.Line);
        Assert.True(WatchTokens.Type.Line > WatchTokens.Type.Small);
        Assert.True(WatchTokens.Type.Small > 0);
    }

    [Fact]
    public void FourHaptics()
    {
        Assert.Equal(4, WatchTokens.Haptics.All.Count);
        Assert.Equal(4, WatchTokens.Haptics.All.Select(pattern => pattern.Name).Distinct().Count());
    }

    [Fact]
    public void HapticShapesAreTheApprovedOnes()
    {
        Assert.Equal(new[] { 500, 250, 500 }, WatchTokens.Haptics.Call.Timings);
        Assert.True(WatchTokens.Haptics.Call.Repeats);
        Assert.Equal(new[] { 80, 120, 80 }, WatchTokens.Haptics.Text.Timings);
        Assert.Equal(new[] { 900 }, WatchTokens.Haptics.RestEnd.Timings);
        Assert.Equal(new[] { 300, 300, 300, 300, 300 }, WatchTokens.Haptics.EaseOff.Timings);
        Assert.All(WatchTokens.Haptics.All, pattern => Assert.Equal(pattern.Timings.Count, pattern.Strengths.Count));
    }

    [Fact]
    public void EaseOffStrongest()
    {
        var easeOff = WatchTokens.Haptics.EaseOff.Strengths.Max();

        Assert.Equal(255, easeOff);
        foreach (var other in WatchTokens.Haptics.All.Where(pattern => pattern != WatchTokens.Haptics.EaseOff))
        {
            Assert.True(other.Strengths.Max() < easeOff, other.Name);
        }
        Assert.All(WatchTokens.Haptics.All.SelectMany(pattern => pattern.Strengths), strength => Assert.InRange(strength, 0, 255));
    }

    [Fact]
    public void MotionIsTheApprovedDurations()
    {
        Assert.Equal(250, WatchTokens.Motion.FadeMs);
        Assert.Equal(1000, WatchTokens.Motion.HourCrossfadeMs);
        Assert.Equal(5000, WatchTokens.Motion.TempoCrossfadeMinMs);
        Assert.Equal(6000, WatchTokens.Motion.TempoCrossfadeMaxMs);
    }

    [Fact]
    public void TypefaceIsPlexMono()
    {
        Assert.Equal("IBM Plex Mono", WatchTokens.Typeface);
    }
}
