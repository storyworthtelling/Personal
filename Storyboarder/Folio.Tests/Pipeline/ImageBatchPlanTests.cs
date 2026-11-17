using Folio.Domain.Pipeline;
using Xunit;

namespace Folio.Tests.Pipeline;

// The scene images on Gemini's batch tier: an inline batch is capped at about 20 MB, and each scene's request
// carries its reference sheets, so a book's images go in several batches, each within a byte budget.
public class ImageBatchPlanTests
{
    private static readonly byte[] Sheet = new byte[600_000];

    [Fact]
    public void AJobsSizeCountsItsPromptAndItsReferencesAsBase64()
    {
        var job = new SceneImageJob(1, "A knight at the gate.", [Sheet, Sheet], 2.0 / 3.0);

        Assert.True(job.ApproxBytes >= 2 * Sheet.Length * 4 / 3);
        Assert.True(job.ApproxBytes < 2 * Sheet.Length * 4 / 3 + 10_000);
    }

    [Fact]
    public void ABatchTakesScenesInOrderUntilTheBudgetIsReached()
    {
        var jobs = Enumerable.Range(1, 10).Select(n => new SceneImageJob(n, "p", [Sheet], null)).ToList();

        var (chunk, tooLarge) = ImageBatchPlan.Chunk(jobs, budget: 3_000_000);

        Assert.Equal([1, 2, 3], chunk.Select(job => job.SceneNumber));
        Assert.Empty(tooLarge);
    }

    /// <summary>A scene too large for any batch on its own goes one at a time; the rest still batch.</summary>
    [Fact]
    public void AnOversizedSceneIsSetAsideAndTheRestStillBatch()
    {
        var huge = new SceneImageJob(1, "p", [new byte[20_000_000]], null);
        var jobs = new[] { huge, new SceneImageJob(2, "p", [Sheet], null), new SceneImageJob(3, "p", [Sheet], null) };

        var (chunk, tooLarge) = ImageBatchPlan.Chunk(jobs, budget: ImageBatchPlan.Budget);

        Assert.Equal([2, 3], chunk.Select(job => job.SceneNumber));
        Assert.Equal([1], tooLarge.Select(job => job.SceneNumber));
    }

    [Fact]
    public void TheBudgetStaysUnderTheInlineLimit() => Assert.True(ImageBatchPlan.Budget < 20_000_000);
}
