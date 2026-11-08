using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Codex.Tests.Storage;

/// <summary>
/// Where filing survives a restart. The ghosts especially: once the original is deleted at
/// the source, the row here is the only copy of that message anywhere, so a store that
/// loses it loses the message outright.
/// </summary>
public class FilingStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection connection;
    private readonly FilingStore store;

    public FilingStoreTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        store = new FilingStore(connection);
    }

    public void Dispose() => connection.Dispose();

    private static TimelineItem Message(string id) =>
        new(TimelineItemKind.Email, "her@example.com", Now, "Seven?", Incoming: true, SourceId: id)
        {
            Subject = "Dinner",
            Body = "Are we still on for seven?",
            MessageId = $"<{id}@example.com>",
        };

    [Fact]
    public void AGhostSurvivesAReopen()
    {
        store.SaveGhost(new Ghost(Message("m1"), Now));

        var reopened = new FilingStore(connection).LoadArchive();

        var ghost = Assert.Single(reopened.All);
        Assert.Equal("m1", ghost.Item.SourceId);
        Assert.Equal(Now, ghost.FiledAtUtc);
    }

    // Everything the message carried has to come back, not just the six universal fields:
    // the ghost is the last copy, so a subject or body dropped here is dropped for good.
    [Fact]
    public void AGhostKeepsTheWholeMessage()
    {
        store.SaveGhost(new Ghost(Message("m1"), Now));

        var ghost = new FilingStore(connection).LoadArchive().All.Single();

        Assert.Equal("Dinner", ghost.Item.Subject);
        Assert.Equal("Are we still on for seven?", ghost.Item.Body);
        Assert.Equal("<m1@example.com>", ghost.Item.MessageId);
        Assert.Equal(TimelineItemKind.Email, ghost.Item.Kind);
    }

    [Fact]
    public void UnfilingRemovesTheGhostFromTheStore()
    {
        store.SaveGhost(new Ghost(Message("m1"), Now));

        store.RemoveGhost("m1", Now);

        Assert.Empty(new FilingStore(connection).LoadArchive().All);
    }

    // The backup is a union now, so an unfiled message must be remembered as unfiled or the next sync
    // brings it back from Drive (error-handling §2).
    [Fact]
    public void UnfilingIsRememberedAndFilingAgainForgetsIt()
    {
        store.SaveGhost(new Ghost(Message("m1"), Now));

        store.RemoveGhost("m1", Now.AddMinutes(5));
        var unfiled = Assert.Single(new FilingStore(connection).LoadUnfiled());

        Assert.Equal("m1", unfiled.SourceId);
        Assert.Equal(Now.AddMinutes(5), unfiled.UnfiledAtUtc);

        store.SaveGhost(new Ghost(Message("m1"), Now.AddMinutes(10)));
        Assert.Empty(store.LoadUnfiled());
    }

    [Fact]
    public void AnUnfiledEntryFromTheBackupIsKeptHere()
    {
        store.RememberUnfiled([new UnfiledGhost("m7", Now), new UnfiledGhost("m8", Now)]);

        Assert.Equal(["m7", "m8"], new FilingStore(connection).LoadUnfiled().Select(u => u.SourceId).Order());
    }

    // Tombstones outlive the process too. A filing whose source delete has not landed must
    // still be suppressed after a restart, or the message walks back into the Mailbox on
    // the next sync — which is the exact failure the tombstone exists to prevent.
    [Fact]
    public void TombstonesSurviveAReopen()
    {
        var book = new Tombstones();
        book.Mark("m1", Now);
        book.AttemptFailed("m1", Now);
        store.SaveTombstones(book);

        var reopened = new FilingStore(connection).LoadTombstones();

        Assert.True(reopened.IsFiled("m1"));
        Assert.Equal(1, reopened.FailedAttempts("m1"));
    }

    // Saving is a replace, not an append: a confirmed delete drops its tombstone, and the
    // store has to forget it rather than keep suppressing a message forever.
    [Fact]
    public void SavingReplacesRatherThanAccumulates()
    {
        var book = new Tombstones();
        book.Mark("m1", Now);
        book.Mark("m2", Now);
        store.SaveTombstones(book);

        book.SourceConfirmed("m1");
        store.SaveTombstones(book);

        var reopened = new FilingStore(connection).LoadTombstones();
        Assert.False(reopened.IsFiled("m1"));
        Assert.True(reopened.IsFiled("m2"));
    }

    [Fact]
    public void AnEmptyStoreLoadsCleanly()
    {
        Assert.Empty(store.LoadArchive().All);
        Assert.Empty(store.LoadTombstones().Outstanding());
    }

    // Filed is searched rather than listed, so the search reads a short summary of each
    // ghost — never the body in full — and only what has settled past its undo window.
    [Fact]
    public void EntriesSummariseEachSettledGhost()
    {
        var sent = Now.AddDays(-3);
        store.SaveGhost(new Ghost(Message("m1") with { TimestampUtc = sent }, Now.AddMinutes(-5)));
        store.SaveGhost(new Ghost(Message("fresh"), Now));
        store.SaveGhost(new Ghost(
            new TimelineItem(TimelineItemKind.Message, "5550101", sent, null, Incoming: false, SourceId: "t1")
            {
                Body = new string('w', 5_000),
            },
            Now.AddMinutes(-5)));

        var entries = store.LoadEntries(settledBy: Now.AddSeconds(-10)).OrderBy(e => e.SourceId).ToList();

        Assert.Equal(["m1", "t1"], entries.Select(e => e.SourceId));
        var mail = entries[0];
        Assert.Equal(TimelineItemKind.Email, mail.Kind);
        Assert.True(mail.Incoming);
        Assert.Equal("her@example.com", mail.ContactKey);
        Assert.Equal("her@example.com", mail.Who);
        Assert.Equal("Dinner", mail.Subject);
        Assert.Equal("Seven?", mail.Excerpt);
        Assert.Equal(sent, mail.SentAtUtc);
        Assert.Equal(Now.AddMinutes(-5), mail.FiledAtUtc);

        var text = entries[1];
        Assert.False(text.Incoming);
        Assert.Null(text.Subject);
        Assert.True(text.Excerpt!.Length <= FilingStore.ExcerptChars); // the body, cut short
    }

    [Fact]
    public void GhostsLoadByIdInTheOrderAsked()
    {
        store.SaveGhost(new Ghost(Message("m1"), Now));
        store.SaveGhost(new Ghost(Message("m2"), Now));
        store.SaveGhost(new Ghost(Message("m3"), Now));

        var ghosts = store.LoadGhosts(["m3", "gone", "m1"]);

        Assert.Equal(["m3", "m1"], ghosts.Select(g => g.Item.SourceId));
        Assert.Equal("Are we still on for seven?", ghosts[0].Item.Body);
    }
}
