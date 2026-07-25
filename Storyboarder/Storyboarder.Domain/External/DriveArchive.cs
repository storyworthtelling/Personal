namespace Storyboarder.Domain.External;

// Uploads a finished book's outputs to Google Drive. Deliberately narrow — three operations, no Drive
// concepts leaking past it — so the pipeline stage that uses it is testable without the Android head or a
// network, and so the platform implementation has almost no logic in it.
//
// Needs a Drive WRITE scope (drive.file), which the app did not previously request: reading Docs and
// listing files is read-only. Until that scope is granted the archive step is switched off and books
// finish exactly as before.
public interface IDriveArchive
{
    // The folder holding the source manuscript, so the archive lands beside the document it came from
    // rather than in some app-specific corner of the user's Drive. Null when the parent can't be
    // resolved (a shared file, a file in the root of a shared drive), which the caller treats as
    // "nowhere sensible to archive to" rather than an error.
    Task<string?> FindParentFolder(string fileId, CancellationToken cancellationToken);

    // Returns the id of a folder with this name under the parent, creating it only if absent, so
    // re-archiving after a partial upload reuses the folder instead of piling up duplicates.
    Task<string> EnsureFolder(string parentFolderId, string name, CancellationToken cancellationToken);

    // Uploads bytes and returns the new file's id. Replaces any file of the same name in the folder, so a
    // retried upload overwrites its own partial rather than leaving two copies.
    Task<string> UploadFile(
        string folderId, string name, string mimeType, byte[] content, CancellationToken cancellationToken);
}
