using Storyboarder.Domain.Pipeline;
using Xunit;

namespace Storyboarder.Tests.Pipeline;

public class RunEstimatorTests
{
    // The owner's converted novel: ~432K characters over 9 chunks, which became 2162 panels.
    private const int RealNovelChars = 432_000;

    private static RunEstimate Estimate(
        int chars, bool reportOnly = false, int claudePerDay = 500, int imagesPerDay = 100, int sheets = 12) =>
        RunEstimator.Estimate(chars, reportOnly, chunkChars: 48_000, storyboardWindowChars: 8_000,
            claudePerDay, imagesPerDay, sheets);

    [Fact]
    public void Chunks_And_Panels_TrackManuscriptLength()
    {
        var estimate = Estimate(RealNovelChars);

        Assert.Equal(9, estimate.Chunks);
        // Within a sensible band of what that novel actually produced (2162).
        Assert.InRange(estimate.Panels, 1_800, 2_600);
    }

    [Fact]
    public void ImageQuota_SetsTheWallClock_ForAnIllustratedRun()
    {
        // 2160 panels plus sheets against 100 image calls a day is weeks, and that is the number worth
        // seeing before starting — not the few seconds each call takes.
        var estimate = Estimate(RealNovelChars, imagesPerDay: 100);

        Assert.True(estimate.Duration > TimeSpan.FromDays(14),
            $"expected the daily image cap to dominate, got {estimate.Duration}");
        Assert.Equal("image generation", estimate.LimitingFactor);
    }

    [Fact]
    public void RaisingTheImageQuota_ShortensTheRun()
    {
        var slow = Estimate(RealNovelChars, imagesPerDay: 100);
        var fast = Estimate(RealNovelChars, imagesPerDay: 1_000);

        Assert.True(fast.Duration < slow.Duration);
    }

    [Fact]
    public void ReportOnly_SkipsEveryImageAndTheStoryboard()
    {
        var estimate = Estimate(RealNovelChars, reportOnly: true);

        Assert.Equal(0, estimate.Panels);
        Assert.Equal(0, estimate.ImageCalls);
        Assert.Equal("text generation", estimate.LimitingFactor);
        // Two calls per chunk plus the two consolidations — no storyboard windows.
        Assert.Equal((9 * 2) + 2, estimate.ClaudeCalls);
        // Cheap enough to run on a draft: hours at most, not weeks.
        Assert.True(estimate.Duration < TimeSpan.FromDays(1), $"expected a quick report run, got {estimate.Duration}");
    }

    [Fact]
    public void EmptyManuscript_EstimatesNothingButTheFixedCalls()
    {
        var estimate = Estimate(0);

        Assert.Equal(0, estimate.Chunks);
        Assert.Equal(0, estimate.Panels);
        Assert.Equal(2, estimate.ClaudeCalls);
    }

    [Fact]
    public void NegativeLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Estimate(-1));
    }

    [Fact]
    public void UnlimitedQuota_DoesNotDivideByZero()
    {
        var estimate = RunEstimator.Estimate(RealNovelChars, reportOnly: false, chunkChars: 48_000,
            storyboardWindowChars: 8_000, claudeCallsPerDay: 0, imageCallsPerDay: 0, referenceSheets: 12);

        Assert.Equal(TimeSpan.Zero, estimate.Duration);
    }
}
