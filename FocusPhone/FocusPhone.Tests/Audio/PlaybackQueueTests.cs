using FocusPhone.Domain.Audio;
using Xunit;

namespace FocusPhone.Tests.Audio;

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
}
