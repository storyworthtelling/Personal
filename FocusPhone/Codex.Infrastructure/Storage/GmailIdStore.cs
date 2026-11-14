using Codex.Infrastructure.Email;
using Microsoft.Data.Sqlite;

namespace Codex.Infrastructure.Storage;

/// <summary>
/// The Gmail ids this phone has fetched, each with the source id its message is cached under (efficiency audit §2).
/// A message listed again is taken as held and not downloaded, which is also why aged-out mail, deleted from the
/// cache but still in Gmail, is no longer fetched again on every reconcile. Lives in <c>timeline.db</c>, so it goes
/// with the cache it describes.
/// </summary>
public sealed class GmailIdStore : IGmailIdIndex
{
    private readonly SqliteConnection connection;

    public GmailIdStore(SqliteConnection connection)
    {
        this.connection = connection;
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS gmailIds (
                gmailId TEXT NOT NULL PRIMARY KEY,
                sourceId TEXT NOT NULL
            );
            """;
        create.ExecuteNonQuery();
    }

    public string? SourceIdFor(string gmailId)
    {
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT sourceId FROM gmailIds WHERE gmailId = $id";
        select.Parameters.AddWithValue("$id", gmailId);
        return select.ExecuteScalar() as string;
    }

    public void Remember(string gmailId, string sourceId)
    {
        using var upsert = connection.CreateCommand();
        upsert.CommandText = """
            INSERT INTO gmailIds (gmailId, sourceId) VALUES ($id, $source)
            ON CONFLICT(gmailId) DO UPDATE SET sourceId = excluded.sourceId
            """;
        upsert.Parameters.AddWithValue("$id", gmailId);
        upsert.Parameters.AddWithValue("$source", sourceId);
        upsert.ExecuteNonQuery();
    }
}

/// <summary>
/// The ids a sync learns, held until its messages are in the cache (<see cref="Commit"/>): an id stored before its
/// message was would be taken as held by the next sync, and the message never fetched again.
/// </summary>
public sealed class PendingGmailIds(IGmailIdIndex store) : IGmailIdIndex
{
    private readonly Dictionary<string, string> learned = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public string? SourceIdFor(string gmailId)
    {
        lock (gate)
        {
            if (learned.TryGetValue(gmailId, out var sourceId))
            {
                return sourceId;
            }
        }
        return store.SourceIdFor(gmailId);
    }

    public void Remember(string gmailId, string sourceId)
    {
        lock (gate)
        {
            learned[gmailId] = sourceId;
        }
    }

    public void Commit()
    {
        lock (gate)
        {
            foreach (var (gmailId, sourceId) in learned)
            {
                store.Remember(gmailId, sourceId);
            }
            learned.Clear();
        }
    }
}
