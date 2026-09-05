using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class SpineSizingTests
{
    [Fact]
    public void MostUsedApp_IsTheBroadestSpine_UnusedIsSlim()
    {
        var usage = new Dictionary<string, long>
        {
            ["com.a"] = 1000, // the most-used
            ["com.b"] = 300,
            ["com.c"] = 0,    // never used
        };

        var sizes = SpineSizing.Rank(usage, ["com.a", "com.b", "com.c"]);

        Assert.Equal(SpineSizing.Size.Broad, sizes["com.a"]);
        Assert.Equal(SpineSizing.Size.Slim, sizes["com.c"]);
        // b sits between: not slim, not broad.
        Assert.True(sizes["com.b"] is SpineSizing.Size.Regular or SpineSizing.Size.Wide);
    }

    [Fact]
    public void AllZeroUsage_EveryonesSlim_NoUsageAccessGranted()
    {
        // UsageStatsManager returns nothing (permission not granted) → uniform slim spines.
        var sizes = SpineSizing.Rank(new Dictionary<string, long>(), ["com.a", "com.b"]);

        Assert.Equal(SpineSizing.Size.Slim, sizes["com.a"]);
        Assert.Equal(SpineSizing.Size.Slim, sizes["com.b"]);
    }

    [Fact]
    public void EveryRequestedPackage_GetsASize_EvenWhenAbsentFromUsage()
    {
        var usage = new Dictionary<string, long> { ["com.a"] = 500 };

        var sizes = SpineSizing.Rank(usage, ["com.a", "com.missing"]);

        Assert.Equal(SpineSizing.Size.Broad, sizes["com.a"]);
        Assert.Equal(SpineSizing.Size.Slim, sizes["com.missing"]);
    }

    [Fact]
    public void Buckets_ScaleToTheBusiestApp_NotAnAbsoluteThreshold()
    {
        // Small absolute numbers still spread across buckets — sizing is relative to the max.
        var usage = new Dictionary<string, long> { ["com.a"] = 9, ["com.b"] = 6, ["com.c"] = 3, ["com.d"] = 1 };

        var sizes = SpineSizing.Rank(usage, ["com.a", "com.b", "com.c", "com.d"]);

        Assert.Equal(SpineSizing.Size.Broad, sizes["com.a"]);   // ratio 1.0
        Assert.Equal(SpineSizing.Size.Wide, sizes["com.b"]);    // ratio ~0.67
        Assert.Equal(SpineSizing.Size.Regular, sizes["com.c"]); // ratio ~0.33
        Assert.Equal(SpineSizing.Size.Slim, sizes["com.d"]);    // ratio ~0.11
    }
}
