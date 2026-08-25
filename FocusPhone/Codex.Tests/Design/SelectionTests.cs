using Codex.Domain.Design;
using Xunit;

namespace Codex.Tests.Design;

public class SelectionTests
{
    [Fact]
    public void New_IsEmptyAndInactive()
    {
        var selection = new Selection();

        Assert.False(selection.IsActive);
        Assert.Equal(0, selection.Count);
        Assert.Empty(selection.Items);
    }

    [Fact]
    public void Begin_ActivatesWithTheFirstItem()
    {
        var selection = new Selection();

        selection.Begin("a");

        Assert.True(selection.IsActive);
        Assert.Equal(1, selection.Count);
        Assert.True(selection.Contains("a"));
    }

    [Fact]
    public void Toggle_AddsThenRemoves()
    {
        var selection = new Selection();
        selection.Begin("a");

        selection.Toggle("b");
        Assert.Equal(2, selection.Count);

        selection.Toggle("b");
        Assert.Equal(1, selection.Count);
        Assert.False(selection.Contains("b"));
    }

    // Order is what the queue receives, so it has to be the order they were picked
    // rather than whatever order the list happened to be in.
    [Fact]
    public void Items_KeepPickOrder()
    {
        var selection = new Selection();
        selection.Begin("c");
        selection.Toggle("a");
        selection.Toggle("b");

        Assert.Equal(["c", "a", "b"], selection.Items);
    }

    [Fact]
    public void Toggle_ThenReAdd_MovesToTheEnd()
    {
        var selection = new Selection();
        selection.Begin("a");
        selection.Toggle("b");
        selection.Toggle("a");
        selection.Toggle("a");

        Assert.Equal(["b", "a"], selection.Items);
    }

    // Deselecting the last item leaves the picker open rather than dropping you out
    // of it mid-task — the way out is Cancel, which is on screen.
    [Fact]
    public void EmptyingTheSelection_StaysActive()
    {
        var selection = new Selection();
        selection.Begin("a");

        selection.Toggle("a");

        Assert.True(selection.IsActive);
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void Cancel_ClearsAndDeactivates()
    {
        var selection = new Selection();
        selection.Begin("a");
        selection.Toggle("b");

        selection.Cancel();

        Assert.False(selection.IsActive);
        Assert.Equal(0, selection.Count);
    }

    // The approved rule: filter to "ferry", pick four, clear the filter — the four
    // stay picked. Selection is about the items, not about what is on screen.
    [Fact]
    public void Selection_SurvivesTheVisibleSetChanging()
    {
        var selection = new Selection();
        selection.Begin("ferry-1");
        selection.Toggle("ferry-2");

        selection.RetainKnown(["unrelated-1", "unrelated-2", "ferry-1", "ferry-2"]);

        Assert.Equal(2, selection.Count);
    }

    // But an item that has genuinely gone — unsubscribed, deleted, swept — must not
    // be handed to the queue, so the caller can prune against what still exists.
    [Fact]
    public void RetainKnown_DropsItemsThatNoLongerExist()
    {
        var selection = new Selection();
        selection.Begin("a");
        selection.Toggle("gone");
        selection.Toggle("b");

        selection.RetainKnown(["a", "b"]);

        Assert.Equal(["a", "b"], selection.Items);
    }

    [Fact]
    public void RetainKnown_KeepsPickOrder()
    {
        var selection = new Selection();
        selection.Begin("c");
        selection.Toggle("a");

        selection.RetainKnown(["a", "b", "c"]);

        Assert.Equal(["c", "a"], selection.Items);
    }

    [Fact]
    public void Begin_OnAnActiveSelection_AddsRatherThanRestarting()
    {
        var selection = new Selection();
        selection.Begin("a");

        selection.Begin("b");

        Assert.Equal(["a", "b"], selection.Items);
    }
}
