using Microsoft.Data.Sqlite;

namespace Codex.Infrastructure.Storage;

/// <summary>
/// Persists the owner's manual identity overrides: pairs of contact keys they
/// joined into one person, and keys they pinned standalone (unlinked). Combined
/// with the contact-card associations read live from ContactsContract, these
/// build the Domain <see cref="Domain.Timeline.IdentityMap"/>.
/// </summary>
public class IdentityLinkStore
{
    private readonly SqliteConnection connection;

    public IdentityLinkStore(SqliteConnection connection)
    {
        this.connection = connection;
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS identityLinks (
                keyA TEXT NOT NULL,
                keyB TEXT NOT NULL,
                PRIMARY KEY (keyA, keyB)
            );
            CREATE TABLE IF NOT EXISTS identityStandalone (
                key TEXT NOT NULL PRIMARY KEY
            );
            """;
        create.ExecuteNonQuery();
    }

    public void Link(string keyA, string keyB)
    {
        // Store the pair ordered so (a,b) and (b,a) are the same row, and clear
        // any standalone pin on either key — an explicit link overrides a prior unlink.
        var (first, second) = string.CompareOrdinal(keyA, keyB) <= 0 ? (keyA, keyB) : (keyB, keyA);
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            DELETE FROM identityStandalone WHERE key IN ($a, $b);
            INSERT OR IGNORE INTO identityLinks (keyA, keyB) VALUES ($first, $second);
            """;
        insert.Parameters.AddWithValue("$a", keyA);
        insert.Parameters.AddWithValue("$b", keyB);
        insert.Parameters.AddWithValue("$first", first);
        insert.Parameters.AddWithValue("$second", second);
        insert.ExecuteNonQuery();
    }

    public void Unlink(string key)
    {
        // Pin the key standalone and drop any manual links naming it; contact-card
        // associations are overridden at resolve time by the standalone set.
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM identityLinks WHERE keyA = $key OR keyB = $key;
            INSERT OR IGNORE INTO identityStandalone (key) VALUES ($key);
            """;
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<(string, string)> LoadLinks()
    {
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT keyA, keyB FROM identityLinks";
        using var reader = select.ExecuteReader();

        var links = new List<(string, string)>();
        while (reader.Read())
        {
            links.Add((reader.GetString(0), reader.GetString(1)));
        }
        return links;
    }

    public IReadOnlyList<string> LoadStandalone()
    {
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT key FROM identityStandalone";
        using var reader = select.ExecuteReader();

        var keys = new List<string>();
        while (reader.Read())
        {
            keys.Add(reader.GetString(0));
        }
        return keys;
    }
}
