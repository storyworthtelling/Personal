using Codex.Domain.Timeline;
using Codex.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Codex.Tests.Storage;

/// <summary>
/// Orphan audit §25: Unlink had no caller, so an address the address book wrongly joined to someone could
/// not be taken out of their correspondence. The Mailbox now separates one, undo-first, and Rejoin is the undo.
/// </summary>
public sealed class IdentityLinkStoreTests : IDisposable
{
    private readonly SqliteConnection connection;

    public IdentityLinkStoreTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
    }

    public void Dispose() => connection.Dispose();

    private static IdentityMap Map(IdentityLinkStore store, params string[][] cards) =>
        new(cards, store.LoadLinks(), store.LoadStandalone());

    [Fact]
    public void ASeparatedAddressLeavesTheCardItWasJoinedBy()
    {
        var store = new IdentityLinkStore(connection);
        string[] card = ["5550101", "sam@example.com"];

        store.Unlink("sam@example.com");

        var map = Map(store, card);
        Assert.NotEqual(map.ResolveThreadKey("5550101"), map.ResolveThreadKey("sam@example.com"));
    }

    [Fact]
    public void RejoinUndoesTheSeparation()
    {
        var store = new IdentityLinkStore(connection);
        string[] card = ["5550101", "sam@example.com"];
        store.Unlink("sam@example.com");

        store.Rejoin("sam@example.com");

        var map = Map(store, card);
        Assert.Equal(map.ResolveThreadKey("5550101"), map.ResolveThreadKey("sam@example.com"));
    }
}
