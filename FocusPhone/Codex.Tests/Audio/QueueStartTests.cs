using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

public class QueueStartTests
{
    private static bool Playable(string id) => !id.StartsWith("gone", StringComparison.Ordinal);

    [Fact]
    public void Locate_KeepsTheQueueOrderAndFindsTheChosenItem()
    {
        var start = QueueStart.Locate(["a", "b", "c"], Playable, "b");

        Assert.True(start.Found);
        Assert.Equal(["a", "b", "c"], start.Ids);
        Assert.Equal(1, start.Index);
    }

    // Pruning has to happen before the index is taken, or the player is handed a
    // shorter list than the index was computed against and starts on the wrong track.
    [Fact]
    public void Locate_DropsUnplayableItemsAndIndexesTheSurvivors()
    {
        var start = QueueStart.Locate(["gone-1", "a", "gone-2", "b"], Playable, "b");

        Assert.Equal(["a", "b"], start.Ids);
        Assert.Equal(1, start.Index);
    }

    // The chosen item itself being unplayable is a different failure from it being
    // absent, and the screen says which — so Found is false but the pruned queue
    // still comes back, because the rest of it is still good.
    [Fact]
    public void Locate_ReportsAMissWhenTheChosenItemCannotPlay()
    {
        var start = QueueStart.Locate(["a", "gone-1", "b"], Playable, "gone-1");

        Assert.False(start.Found);
        Assert.Equal(["a", "b"], start.Ids);
    }

    [Fact]
    public void Locate_ReportsAMissWhenTheChosenItemIsNotQueued()
    {
        var start = QueueStart.Locate(["a", "b"], Playable, "c");

        Assert.False(start.Found);
        Assert.Equal(["a", "b"], start.Ids);
    }

    [Fact]
    public void Locate_SurvivesAnEmptyQueue()
    {
        var start = QueueStart.Locate([], Playable, "a");

        Assert.False(start.Found);
        Assert.Empty(start.Ids);
    }

    // Duplicates would make the index ambiguous; the queue de-dupes on the way in,
    // so this pins that Locate does not reintroduce them.
    [Fact]
    public void Locate_KeepsTheFirstOfADuplicatedId()
    {
        var start = QueueStart.Locate(["a", "b", "a"], Playable, "a");

        Assert.Equal(["a", "b"], start.Ids);
        Assert.Equal(0, start.Index);
    }
}
