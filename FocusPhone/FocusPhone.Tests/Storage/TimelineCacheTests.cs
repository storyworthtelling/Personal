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
