using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Timeline;

public class DeleteBufferTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(6);

    private static TimelineItem Item(string sourceId, TimelineItemKind kind = TimelineItemKind.Message) =>
        new(kind, "alice", Now, "hi", Incoming: true, SourceId: sourceId);

    [Fact]
    public void ScheduledDelete_HidesItemFromView()
    {
        var buffer = new DeleteBuffer();
        var item = Item("m1");
        buffer.Schedule(item, Now, Window);

        Assert.DoesNotContain(item, buffer.Filter([item]));
        Assert.True(buffer.IsPending(item));
    }

    [Fact]
    public void Undo_RestoresItemAndCancelsTheDelete()
    {
        var buffer = new DeleteBuffer();
        var item = Item("m1");
        buffer.Schedule(item, Now, Window);

        Assert.True(buffer.Undo(item.Kind, item.SourceId));

        Assert.Contains(item, buffer.Filter([item]));
        Assert.Empty(buffer.TakeDue(Now + Window + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void TakeDue_ReturnsNothingBeforeTheWindowElapses()
    {
        var buffer = new DeleteBuffer();
        buffer.Schedule(Item("m1"), Now, Window);

        Assert.Empty(buffer.TakeDue(Now + TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void TakeDue_ReturnsAndClearsTheDeleteOnceTheWindowElapses()
    {
        var buffer = new DeleteBuffer();
        buffer.Schedule(Item("m1"), Now, Window);

        var due = buffer.TakeDue(Now + Window);

        var entry = Assert.Single(due);
        Assert.Equal("m1", entry.SourceId);
        Assert.Empty(buffer.Pending);
    }

    [Fact]
    public void DifferentKinds_WithSameSourceId_AreTrackedSeparately()
    {
        var buffer = new DeleteBuffer();
        var text = Item("shared", TimelineItemKind.Message);
        var call = Item("shared", TimelineItemKind.Call);
        buffer.Schedule(text, Now, Window);

        Assert.True(buffer.IsPending(text));
        Assert.False(buffer.IsPending(call));
    }
}
