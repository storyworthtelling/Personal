using Microsoft.Data.Sqlite;

namespace FocusPhone.Infrastructure.Storage;

/// <summary>
/// Persists a per-thread last-read timestamp. Opening a thread stamps "now";
/// the Domain <see cref="Domain.Timeline.ReadState"/> uses the stamps to decide
/// which threads still hold something the owner hasn't seen.
/// </summary>
public class ReadStateStore
{
    private readonly SqliteConnection connection;

    public ReadStateStore(SqliteConnection connection)
    {
        this.connection = connection;
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS threadRead (
                threadKey TEXT NOT NULL PRIMARY KEY,
                lastReadUtc TEXT NOT NULL
            )
            """;
        create.ExecuteNonQuery();
    }

    public void MarkRead(string threadKey, DateTimeOffset when)
    {
        using var upsert = connection.CreateCommand();
        upsert.CommandText = """
            INSERT INTO threadRead (threadKey, lastReadUtc) VALUES ($threadKey, $lastReadUtc)
            ON CONFLICT (threadKey) DO UPDATE SET lastReadUtc = excluded.lastReadUtc
            """;
        upsert.Parameters.AddWithValue("$threadKey", threadKey);
        upsert.Parameters.AddWithValue("$lastReadUtc", when.ToString("O"));
        upsert.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, DateTimeOffset> LoadAll()
    {
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT threadKey, lastReadUtc FROM threadRead";
        using var reader = select.ExecuteReader();

        var map = new Dictionary<string, DateTimeOffset>();
        while (reader.Read())
        {
            map[reader.GetString(0)] = DateTimeOffset.Parse(reader.GetString(1));
        }
        return map;
    }
}
