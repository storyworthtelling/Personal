namespace Storyworthtelling.Voice.Logs;

/// <summary>
/// The log loop's Drive folder (LOG-LOOP-PLAN.md §3.4), beside <see cref="IDriveTextFile"/>: where that is
/// one named file with overwrite, this is many named files that are created once and never overwritten.
/// A seam so <see cref="LogUploader"/> is tested without Google, and so the Drive glue stays thin.
/// </summary>
public interface IDriveLogFolder
{
    /// <summary>The id of the file with this exact name in the folder, or null.</summary>
    Task<string?> FindAsync(string name, CancellationToken cancellationToken);

    /// <summary>Creates a new file in the folder (making the folder first if need be) and returns its id.</summary>
    Task<string> CreateAsync(string name, string content, CancellationToken cancellationToken);

    /// <summary>A file's content by id, or null when it no longer exists.</summary>
    Task<string?> ReadAsync(string id, CancellationToken cancellationToken);

    /// <summary>The names the reader has processed (§5.6), for a phone whose files only it can trash.</summary>
    Task<IReadOnlyList<string>> ProcessedNamesAsync(CancellationToken cancellationToken);

    /// <summary>Trashes the named file if this account owns it; never a permanent delete (A1).</summary>
    Task TrashOwnAsync(string name, CancellationToken cancellationToken);
}
