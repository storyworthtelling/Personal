using Android.Content;
using FocusPhone.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace FocusPhone.Communication;

/// <summary>
/// Opens the app-private SQLite database backing the timeline cache.
/// Dispose after each unit of work; SQLite handles cross-process writes from
/// the notification listener and sync worker via locking.
/// </summary>
public sealed class TimelineDatabase : IDisposable
{
    private readonly SqliteConnection connection;

    public TimelineCache Cache { get; }

    private TimelineDatabase(SqliteConnection connection)
    {
        this.connection = connection;
        Cache = new TimelineCache(connection);
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
