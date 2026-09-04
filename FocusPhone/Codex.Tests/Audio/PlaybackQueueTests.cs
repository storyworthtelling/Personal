using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

public class PlaybackQueueTests
{
    [Fact]
    public void Add_AppendsInOrder()
    {
        var queue = new PlaybackQueue();

        queue.Add("a");
        queue.Add("b");

        Assert.Equal(["a", "b"], queue.Items);
    }

    [Fact]
    public void Add_IgnoresDuplicates()
    {
        var queue = new PlaybackQueue(["a"]);

        queue.Add("a");

        Assert.Equal(["a"], queue.Items);
    }

    [Fact]
    public void Remove_DropsTheItem()
    {
        var queue = new PlaybackQueue(["a", "b", "c"]);

        queue.Remove("b");

        Assert.Equal(["a", "c"], queue.Items);
    }

    [Fact]
    public void Move_ReordersItems()
    {
        var queue = new PlaybackQueue(["a", "b", "c"]);

        queue.Move(0, 2);

        Assert.Equal(["b", "c", "a"], queue.Items);
    }

    [Fact]
    public void Move_ClampsOutOfRangeTarget()
    {
        var queue = new PlaybackQueue(["a", "b", "c"]);

        queue.Move(2, 99);

        Assert.Equal(["a", "b", "c"], queue.Items);
    }

    [Fact]
    public void ReplaceWith_ReordersAndDedupes()
    {
        var queue = new PlaybackQueue(["a", "b"]);

        queue.ReplaceWith(["c", "a", "c", "b"]);

        Assert.Equal(["c", "a", "b"], queue.Items);
    }

    [Fact]
    public void Constructor_DedupesInitialItems()
    {
        var queue = new PlaybackQueue(["a", "b", "a"]);

        Assert.Equal(["a", "b"], queue.Items);
    }

    [Fact]
    public void Queue_SurvivesExportAndRestore()
    {
        var queue = new PlaybackQueue(["a", "b", "c"]);
        queue.Move(2, 0);

        var restored = new PlaybackQueue(queue.Export());

        Assert.Equal(["c", "a", "b"], restored.Items);
    }

    // "Play next" — the queue's other insertion point. What is playing keeps playing;
    // the picked items land behind it rather than at the end of the run.
    [Fact]
    public void InsertAfter_PutsItemsRightBehindTheAnchor()
    {
        var queue = new PlaybackQueue(["a", "b", "c"]);

        queue.InsertAfter("a", ["x", "y"]);

        Assert.Equal(["a", "x", "y", "b", "c"], queue.Items);
    }

    // Nothing is playing, or what is playing is not in this queue: next means first.
    [Fact]
    public void InsertAfter_PutsItemsFirstWhenTheAnchorIsUnknown()
    {
        var queue = new PlaybackQueue(["a", "b"]);

        queue.InsertAfter("not here", ["x"]);

        Assert.Equal(["x", "a", "b"], queue.Items);
    }

    [Fact]
    public void InsertAfter_PutsItemsFirstWhenThereIsNoAnchor()
    {
        var queue = new PlaybackQueue(["a"]);

        queue.InsertAfter(null, ["x"]);

        Assert.Equal(["x", "a"], queue.Items);
    }

    // Playing next something already queued later moves it up. Leaving it where it
    // was would make the control look broken; adding a second copy breaks the rule
    // that an id sits in the queue once.
    [Fact]
    public void InsertAfter_MovesSomethingAlreadyQueuedRatherThanRepeatingIt()
    {
        var queue = new PlaybackQueue(["a", "b", "c"]);

        queue.InsertAfter("a", ["c"]);

        Assert.Equal(["a", "c", "b"], queue.Items);
    }

    [Fact]
    public void InsertAfter_KeepsTheOrderItWasGiven()
    {
        var queue = new PlaybackQueue(["a"]);

        queue.InsertAfter("a", ["x", "y", "x"]);

        Assert.Equal(["a", "x", "y"], queue.Items);
    }

    [Fact]
    public void InsertAfter_OnAnEmptyQueueJustFillsIt()
    {
        var queue = new PlaybackQueue();

        queue.InsertAfter("a", ["x", "y"]);

        Assert.Equal(["x", "y"], queue.Items);
    }
}
