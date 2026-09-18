using Codex.Domain.Timeline;
using Codex.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Codex.Tests.Storage;

public class HiddenItemStoreTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly HiddenItemStore store;

    public HiddenItemStoreTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        store = new HiddenItemStore(connection);
    }

    public void Dispose() => connection.Dispose();

    private static TimelineItem Item(string sourceId, TimelineItemKind kind = TimelineItemKind.Message) =>
        new(kind, "alice", new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero), "hi", Incoming: true, sourceId);

    [Fact]
    public void Hide_FiltersTheItemOutOfAMerge()
    {
        store.Hide(TimelineItemKind.Message, "sms:1");

        var visible = store.Filter([Item("sms:1"), Item("sms:2")]);

        Assert.Equal(["sms:2"], visible.Select(i => i.SourceId));
    }

    [Fact]
    public void Hide_IsIdempotent()
    {
        store.Hide(TimelineItemKind.Call, "1");
        store.Hide(TimelineItemKind.Call, "1"); // must not throw on the primary key

        Assert.Single(store.LoadHidden());
    }

    // The un-file counterpart: withdrawing a hide is what stops a restored call or text
    // being filtered right back out of the Mailbox merge on the very next reload.
    [Fact]
    public void Unhide_MakesTheItemVisibleAgain()
    {
        store.Hide(TimelineItemKind.Call, "1");

        store.Unhide(TimelineItemKind.Call, "1");

        var visible = store.Filter([Item("1", TimelineItemKind.Call)]);
        Assert.Single(visible);
        Assert.Empty(store.LoadHidden());
    }

    [Fact]
    public void Unhide_OfSomethingNeverHidden_DoesNothing()
    {
        store.Unhide(TimelineItemKind.Message, "sms:never-hidden");

        Assert.Empty(store.LoadHidden());
    }

    // Kind is part of the key: hiding a call must not hide a message that happens to
    // share the same source id shape.
    [Fact]
    public void HideAndUnhide_AreScopedByKind()
    {
        store.Hide(TimelineItemKind.Call, "1");

        var visible = store.Filter([Item("1", TimelineItemKind.Message)]);

        Assert.Single(visible);
    }
}
