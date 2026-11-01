using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

// Score's scan summary for the log loop (plan §4.5): what each library scan run read, changed and removed, and
// whether it finished the backlog or stopped for its time budget — the line a scan that never finishes leaves.
public class ScanSummaryLineTests
{
    [Fact]
    public void ADrainedRunSaysWhatItFound()
    {
        Assert.Equal("SCAN added 12 · changed 3 · removed 1 · finished",
            new MusicScanRunResult(true, new RescanOutcome(12, 3, 1)).Line());
    }

    [Fact]
    public void ARunStoppedByItsBudgetSaysItWillResume()
    {
        Assert.Equal("SCAN added 400 · changed 0 · removed 0 · stopped for its time budget; resuming",
            new MusicScanRunResult(false, new RescanOutcome(400, 0, 0)).Line());
    }
}
