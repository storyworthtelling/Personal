namespace Storyworthtelling.Voice;

/// <summary>
/// The file's current revision (Drive's <c>headRevisionId</c>), or null when there is no file. Optional on an
/// <see cref="IDriveTextFile"/>: <see cref="Secrets.SealedDriveFile"/> compares it before writing, so a file
/// another phone wrote since it was read is a conflict to merge again, not something to write over.
/// </summary>
public interface IDriveFileRevision
{
    Task<string?> RevisionAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Moves the file to Drive's trash (recoverable for 30 days). Optional on an <see cref="IDriveTextFile"/>:
/// <see cref="Secrets.SealedDriveFile"/> uses it to take back a first upload that did not read back, so a
/// damaged file is never left where the next read would refuse to write over it.
/// </summary>
public interface IDriveFileTrash
{
    Task TrashAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Whether this install has found the file in Drive before (it remembers the file's id). Optional on an
/// <see cref="IDriveTextFile"/>: <see cref="Secrets.SealedDriveFile"/> refuses to create a file such an install
/// suddenly cannot find, since a search miss or a narrowed scope would otherwise create a second copy beside the
/// real one, which every other install then prefers or never sees (contracts M6).
/// </summary>
public interface IDriveFileMemory
{
    bool HasFoundBefore { get; }
}
