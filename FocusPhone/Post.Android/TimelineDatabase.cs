using Android.Content;
using Codex.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Post;

/// <summary>
/// Opens the app-private SQLite database backing the timeline cache.
/// Dispose after each unit of work; SQLite handles cross-process writes from
/// the notification listener and sync worker via locking.
/// </summary>
public sealed class TimelineDatabase : IDisposable
{
    private readonly SqliteConnection connection;

    public TimelineCache Cache { get; }
    public HiddenItemStore Hidden { get; }
    public IdentityLinkStore IdentityLinks { get; }
    public ReadStateStore ReadState { get; }

    /// <summary>
    /// The ghost archive and the tombstones. Separate tables in the same file: the cache
    /// mirrors the source and is rebuilt from it, while these are the opposite — once a
    /// message is deleted at the source the ghost is the only copy anywhere. Same file so
    /// there is one thing to back up, which timeline.db already is.
    /// </summary>
    public FilingStore Filing { get; }

    private TimelineDatabase(SqliteConnection connection)
    {
        this.connection = connection;
        Cache = new TimelineCache(connection);
        Hidden = new HiddenItemStore(connection);
        IdentityLinks = new IdentityLinkStore(connection);
        ReadState = new ReadStateStore(connection);
        Filing = new FilingStore(connection);
    }

    public static TimelineDatabase Open(Context context)
    {
        var path = Path.Combine(context.FilesDir!.AbsolutePath, "timeline.db");
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return new TimelineDatabase(connection);
    }

    public void Dispose() => connection.Dispose();
}
