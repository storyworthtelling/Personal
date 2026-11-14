using Codex.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Codex.Tests.Storage;

// The phone's record of which Gmail ids it already holds, so a reconcile fetches only what is new (efficiency §2).
public class GmailIdStoreTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public GmailIdStoreTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    [Fact]
    public void ARememberedIdGivesBackItsSourceId()
    {
        var store = new GmailIdStore(connection);
        store.Remember("g1", "<m1@example.com>");

        Assert.Equal("<m1@example.com>", store.SourceIdFor("g1"));
        Assert.Null(store.SourceIdFor("g2"));
    }

    [Fact]
    public void ItSurvivesReopening()
    {
        new GmailIdStore(connection).Remember("g1", "<m1@example.com>");

        Assert.Equal("<m1@example.com>", new GmailIdStore(connection).SourceIdFor("g1"));
    }
}

// Ids learned during a fetch are written only once the messages are in the cache, so a sync that dies between the
// two fetches them again next time rather than taking them as held.
public class PendingGmailIdsTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public PendingGmailIdsTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    [Fact]
    public void ALearnedIdIsHeldButNotStoredUntilCommitted()
    {
        var store = new GmailIdStore(connection);
        var pending = new PendingGmailIds(store);

        pending.Remember("g1", "m1@example.com");

        Assert.Equal("m1@example.com", pending.SourceIdFor("g1"));
        Assert.Null(store.SourceIdFor("g1"));

        pending.Commit();

        Assert.Equal("m1@example.com", store.SourceIdFor("g1"));
    }
}
