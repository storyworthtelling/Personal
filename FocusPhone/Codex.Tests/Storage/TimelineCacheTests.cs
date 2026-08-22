using Codex.Domain.Timeline;
using Codex.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Codex.Tests.Storage;

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

    [Fact]
    public void Upsert_VoicemailKind_RoundTrips()
    {
        cache.Upsert([Item("vm:5551234567:1", TimelineItemKind.Voicemail, snippet: "Hi, it's mom")]);

        var item = Assert.Single(cache.LoadAll());
        Assert.Equal(TimelineItemKind.Voicemail, item.Kind);
        Assert.Equal("Hi, it's mom", item.Snippet);
    }

    [Fact]
    public void Upsert_EmailWithHeadersAndAttachments_RoundTrips()
    {
        var email = new TimelineItem(
            TimelineItemKind.Email, "bob@example.com",
            new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero), "Subject line", Incoming: false, "e1")
        {
            Subject = "Subject line",
            Body = "Body text",
            FromAddress = "me@gmail.com",
            Recipients = ["bob@example.com", "carol@example.com"],
            CarbonCopy = ["dave@example.com"],
            MessageId = "abc@mail",
            InReplyTo = "prev@mail",
            Attachments = [new AttachmentRef("a1", "photo.jpg", "image/jpeg", 0)],
        };

        cache.Upsert([email]);
        var loaded = Assert.Single(cache.LoadAll());

        Assert.Equal("Subject line", loaded.Subject);
        Assert.Equal("Body text", loaded.Body);
        Assert.Equal("me@gmail.com", loaded.FromAddress);
        Assert.Equal(["bob@example.com", "carol@example.com"], loaded.Recipients);
        Assert.Equal(["dave@example.com"], loaded.CarbonCopy);
        Assert.Equal("abc@mail", loaded.MessageId);
        Assert.Equal("prev@mail", loaded.InReplyTo);
        var attachment = Assert.Single(loaded.Attachments);
        Assert.Equal("photo.jpg", attachment.FileName);
        Assert.True(attachment.IsImage);
    }

    [Fact]
    public void Upsert_GroupMessageWithUnread_RoundTrips()
    {
        var group = new TimelineItem(
            TimelineItemKind.Message, "5551110000",
            new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero), "hi all", Incoming: true, "g1")
        {
            GroupId = "conv-42",
            Participants = ["5551110000", "5552220000"],
            ExternalConversationId = "conv-42",
            Unread = true,
        };

        cache.Upsert([group]);
        var loaded = Assert.Single(cache.LoadAll());

        Assert.Equal("conv-42", loaded.GroupId);
        Assert.True(loaded.IsGroup);
        Assert.Equal(["5551110000", "5552220000"], loaded.Participants);
        Assert.Equal("conv-42", loaded.ExternalConversationId);
        Assert.True(loaded.Unread);
    }

    [Fact]
    public void LoadRecent_ReturnsNewestFirst_AndHonorsLimit()
    {
        cache.Upsert(
        [
            Item("m1", minute: 1),
            Item("m2", minute: 2),
            Item("m3", minute: 3),
            Item("m4", minute: 4),
        ]);

        var page = cache.LoadRecent(2);

        Assert.Equal(["m4", "m3"], page.Select(i => i.SourceId));
    }

    [Fact]
    public void LoadRecent_WithOffset_PagesThroughOlderItems()
    {
        cache.Upsert(
        [
            Item("m1", minute: 1),
            Item("m2", minute: 2),
            Item("m3", minute: 3),
            Item("m4", minute: 4),
        ]);

        var first = cache.LoadRecent(2, offset: 0);
        var second = cache.LoadRecent(2, offset: 2);

        Assert.Equal(["m4", "m3"], first.Select(i => i.SourceId));
        Assert.Equal(["m2", "m1"], second.Select(i => i.SourceId));
    }

    [Fact]
    public void LoadRecent_OffsetPastEnd_ReturnsEmpty()
    {
        cache.Upsert([Item("m1"), Item("m2", minute: 1)]);

        Assert.Empty(cache.LoadRecent(10, offset: 2));
    }

    [Fact]
    public void ReconcileWindow_RemovesInWindowItemsAbsentFromAuthoritative()
    {
        // "kept" and "stale" are both cached; only "kept" is still on the server.
        cache.Upsert(
        [
            Item("kept", TimelineItemKind.Email, minute: 10),
            Item("stale", TimelineItemKind.Email, minute: 11),
        ]);

        cache.ReconcileWindow(TimelineItemKind.Email,
            new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero),
            [Item("kept", TimelineItemKind.Email, minute: 10)]);

        var item = Assert.Single(cache.LoadAll());
        Assert.Equal("kept", item.SourceId);
    }

    [Fact]
    public void ReconcileWindow_UpsertsNewAuthoritativeItems()
    {
        cache.ReconcileWindow(TimelineItemKind.Email,
            new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero),
            [Item("fresh", TimelineItemKind.Email, minute: 30, snippet: "new mail")]);

        var item = Assert.Single(cache.LoadAll());
        Assert.Equal("fresh", item.SourceId);
        Assert.Equal("new mail", item.Snippet);
    }

    [Fact]
    public void ReconcileWindow_KeepsItemsOlderThanTheWindow()
    {
        // An item from before "since" must survive even though the authoritative
        // fetch (which only covers the window) does not include it.
        cache.Upsert([Item("old", TimelineItemKind.Email, minute: 0)]);

        cache.ReconcileWindow(TimelineItemKind.Email,
            new DateTimeOffset(2026, 6, 10, 9, 30, 0, TimeSpan.Zero),
            []);

        Assert.Single(cache.LoadAll(), i => i.SourceId == "old");
    }

    [Fact]
    public void ReconcileWindow_LeavesOtherKindsUntouched()
    {
        cache.Upsert(
        [
            Item("call", TimelineItemKind.Call, minute: 40),
            Item("text", TimelineItemKind.Message, minute: 41),
        ]);

        cache.ReconcileWindow(TimelineItemKind.Email,
            new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero),
            []);

        Assert.Equal(2, cache.LoadAll().Count);
    }

    [Fact]
    public void ReconcileWindow_EmptyAuthoritative_ClearsWholeWindow()
    {
        cache.Upsert(
        [
            Item("a", TimelineItemKind.Email, minute: 10),
            Item("b", TimelineItemKind.Email, minute: 11),
        ]);

        cache.ReconcileWindow(TimelineItemKind.Email,
            new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero), []);

        Assert.Empty(cache.LoadAll());
    }

    [Fact]
    public void Constructor_MigratesLegacySixColumnTableAndDefaultsNewFields()
    {
        using var legacy = new SqliteConnection("Data Source=:memory:");
        legacy.Open();
        using (var create = legacy.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE timelineItems (
                    kind INTEGER NOT NULL, sourceId TEXT NOT NULL, contactKey TEXT NOT NULL,
                    timestampUtc TEXT NOT NULL, snippet TEXT, incoming INTEGER NOT NULL,
                    PRIMARY KEY (kind, sourceId))
                """;
            create.ExecuteNonQuery();
        }
        using (var insert = legacy.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO timelineItems (kind, sourceId, contactKey, timestampUtc, snippet, incoming)
                VALUES (1, 'm1', 'alice', '2026-06-10T09:00:00.0000000+00:00', 'hi', 1)
                """;
            insert.ExecuteNonQuery();
        }

        var item = Assert.Single(new TimelineCache(legacy).LoadAll());

        Assert.Equal("alice", item.ContactKey);
        Assert.Null(item.Subject);
        Assert.Empty(item.Recipients);
        Assert.Empty(item.Attachments);
        Assert.False(item.Unread);
    }
}
