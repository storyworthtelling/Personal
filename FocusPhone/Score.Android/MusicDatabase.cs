using Android.Content;
using Codex.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Score;

/// <summary>
/// Opens the app-private SQLite database backing the music catalog cache, so the
/// iPod browse renders instantly and offline. Mirrors the Communication app's
/// TimelineDatabase. Dispose after each unit of work.
/// </summary>
public sealed class MusicDatabase : IDisposable
{
    private readonly SqliteConnection connection;

    public MusicCatalogCache Catalog { get; }

    private MusicDatabase(SqliteConnection connection)
    {
        this.connection = connection;
        Catalog = new MusicCatalogCache(connection);
    }

    public static MusicDatabase Open(Context context)
    {
        var path = Path.Combine(context.FilesDir!.AbsolutePath, "music.db");
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return new MusicDatabase(connection);
    }

    public void Dispose() => connection.Dispose();
}
