using FocusPhone.Domain.Timeline;
using FocusPhone.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusPhone.Tests.Storage;

public class TimelineCacheTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly TimelineCache cache;

    public TimelineCacheTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        cache = new TimelineCache(connection);
    }

    public void Dispose() => connection.Dispose();

    private static TimelineItem Item(string sourceId, TimelineItemKind kind = TimelineItemKind.Message,
        string contactKey = "alice", int minute = 0, string snippet = "hi") =>
        new(kind, contactKey, new DateTimeOffset(2026, 6, 10, 9, minute, 0, TimeSpan.Zero),
            snippet, Incoming: true, sourceId);

    [Fact]
    public void Upsert_ThenLoadAll_RoundTrips()
    {
        cache.Upsert([Item("m1"), Item("e1", TimelineItemKind.Email, minute: 5)]);

        var items = cache.LoadAll();

        Assert.Equal(2, items.Count);
        var email = Assert.Single(items, i => i.Kind == TimelineItemKind.Email);
        Assert.Equal("alice", email.ContactKey);
        Assert.Equal("hi", email.Snippet);
        Assert.True(email.Incoming);
        Assert.Equal(new DateTimeOffset(2026, 6, 10, 9, 5, 0, TimeSpan.Zero), email.TimestampUtc);
    }

    [Fact]
    public void Upsert_SameKindAndSourceId_ReplacesInsteadOfDuplicating()
    {
        cache.Upsert([Item("m1", snippet: "old")]);
        cache.Upsert([Item("m1", snippet: "new")]);

        var item = Assert.Single(cache.LoadAll());
        Assert.Equal("new", item.Snippet);
    }

    [Fact]
    public void Upsert_SameSourceIdDifferentKind_KeepsBoth()
    {
        cache.Upsert([Item("x"), Item("x", TimelineItemKind.Call)]);

        Assert.Equal(2, cache.LoadAll().Count);
    }

    [Fact]
    public void LoadAll_EmptyCache_ReturnsNoItems()
    {
        Assert.Empty(cache.LoadAll());
    }
}
