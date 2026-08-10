using Storyboarder.Domain.Pipeline;
using Xunit;

namespace Storyboarder.Tests.Pipeline;

public class RunEstimatorTests
{
    // A full-length novel: ~432K characters over 9 chunks.
    private const int RealNovelChars = 432_000;

    private static RunEstimate Estimate(
        int chars, int claudePerDay = 500, int imagesPerDay = 100, int sheets = 12) =>
        RunEstimator.Estimate(chars, chunkChars: 48_000, claudePerDay, imagesPerDay, sheets);

    [Fact]
    public void Chunks_And_Scenes_TrackManuscriptLength()
    {
        var estimate = Estimate(RealNovelChars);

        Assert.Equal(9, estimate.Chunks);
        // One evidence image per ~3000 characters: a sensible band for a book this length.
        Assert.InRange(estimate.Scenes, 100, 200);
    }

    [Fact]
    public void ImageQuota_SetsTheWallClock_ForABigBook()
    {
        // ~144 scenes plus sheets against 100 image calls a day is more than a day, and that's the number
        // worth seeing before starting — not the few seconds each call takes.
        var estimate = Estimate(RealNovelChars, imagesPerDay: 100);

        Assert.True(estimate.Duration > TimeSpan.FromDays(1),
            $"expected the daily image cap to dominate, got {estimate.Duration}");
        Assert.Equal("image generation", estimate.LimitingFactor);
    }

    [Fact]
    public void RaisingTheImageQuota_ShortensTheRun()
    {
        var slow = Estimate(RealNovelChars, imagesPerDay: 100);
        var fast = Estimate(RealNovelChars, imagesPerDay: 5_000);

        Assert.True(fast.Duration < slow.Duration);
    }

    [Fact]
    public void EmptyManuscript_EstimatesNothingButTheFixedCalls()
    {
        var estimate = Estimate(0);

        Assert.Equal(0, estimate.Chunks);
        Assert.Equal(0, estimate.Scenes);
        // The fixed reduce calls: story half, craft half, voice consistency.
        Assert.Equal(3, estimate.ClaudeCalls);
    }

    [Fact]
    public void NegativeLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Estimate(-1));
    }

    [Fact]
    public void UnlimitedQuota_DoesNotDivideByZero()
    {
        var estimate = RunEstimator.Estimate(RealNovelChars, chunkChars: 48_000,
            claudeCallsPerDay: 0, imageCallsPerDay: 0, referenceSheets: 12);

        Assert.Equal(TimeSpan.Zero, estimate.Duration);
    }
}
