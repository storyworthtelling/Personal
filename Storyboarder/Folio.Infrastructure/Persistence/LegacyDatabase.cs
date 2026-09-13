namespace Folio.Infrastructure.Persistence;

/// <summary>
/// The database file the app carried before the rename.
///
/// <para>P10 renames <c>storyboarder.db</c> to <c>folio.db</c>, which means the schema reset cannot
/// simply drop "the database": <c>EnsureDeleted()</c> runs against the new context and the old file is
/// not attached to it. Left alone it stays on the device forever — the owner's previous books, invisible
/// and unreachable, on a phone whose storage they are trying to keep clear.</para>
///
/// <para>SQLite leaves two siblings beside the database in WAL mode. Deleting the main file without them
/// is how a "deleted" database comes back as a half-written one, so all three go together.</para>
/// </summary>
public static class LegacyDatabase
{
    public const string FileName = "storyboarder.db";

    /// <summary>Every file the legacy database occupies in <paramref name="directory"/>.</summary>
    public static IReadOnlyList<string> FilesIn(string directory) =>
    [
        Path.Combine(directory, FileName),
        Path.Combine(directory, FileName + "-wal"),
        Path.Combine(directory, FileName + "-shm"),
    ];

    /// <summary>
    /// Deletes them if they are there. Absence is the normal case — a fresh install never had one —
    /// so it is not an error, and a file that cannot be deleted must not take the launch down with it.
    /// </summary>
    public static void Remove(string directory)
    {
        foreach (var file in FilesIn(directory))
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
