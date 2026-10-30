namespace Storyworthtelling.Voice;

// Whether the well-known file this seam names belongs to the signed-in account, or was shared with it.
// A separate seam rather than a member of IDriveTextFile, so a file that cannot report ownership (a test
// fake, a file that never decides anything) is not forced to pretend. The vault store treats a file that
// does not report ownership as Shared — the read-only answer — so an unknown file can never be claimed.
public interface IDriveFileOwnership
{
    // Owned when this account owns the file, Shared when it was shared with this account, None when no
    // such file is visible at all.
    Task<DriveFileOwnership> OwnershipAsync(CancellationToken cancellationToken);
}
