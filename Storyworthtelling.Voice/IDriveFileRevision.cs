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
