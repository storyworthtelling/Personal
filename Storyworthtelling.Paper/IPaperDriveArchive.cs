namespace Storyworthtelling.Paper;

// Uploads a finished document's bytes to Google Drive, beside the source it came from. Deliberately narrow
// — three operations, no Drive concepts leaking past it — so the pipeline stage that uses it is testable
// without an Android head or a network, and so the platform implementation has almost no logic in it.
// Moved here from Canonizer so every app that archives a rendered PDF to Drive shares one seam; delivery is
// composed with the reMarkable transport behind <see cref="IPaperDelivery"/>.
//
// Needs a Drive WRITE scope (drive.file). Until that scope is granted the archive step is switched off and
// documents finish exactly as before.
public interface IPaperDriveArchive
{
    // The folder holding the source document, so the archive lands beside it rather than in some
    // app-specific corner of the user's Drive. Null when the parent can't be resolved (a shared file, a file
    // in the root of a shared drive), which the caller treats as "nowhere sensible to archive to".
    Task<string?> FindParentFolder(string fileId, CancellationToken cancellationToken);

    // Returns the id of a folder with this name under the parent, creating it only if absent, so
    // re-archiving after a partial upload reuses the folder instead of piling up duplicates.
    Task<string> EnsureFolder(string parentFolderId, string name, CancellationToken cancellationToken);

    // Uploads bytes and returns the new file's id. Replaces any file of the same name in the folder, so a
    // retried upload overwrites its own partial rather than leaving two copies.
    Task<string> UploadFile(
        string folderId, string name, string mimeType, byte[] content, CancellationToken cancellationToken);

    // Writes plain text as a Google Doc and returns its id. With the id of a doc written before, the doc is
    // rewritten in place, so Drive keeps each write as a version in the doc's own history; with none, or one
    // that has gone, a new doc is made in the folder. For a document whose changes the owner wants to see over
    // time, not for a finished document (those are UploadFile's).
    Task<string> WriteGoogleDoc(
        string? fileId, string folderId, string name, string text, CancellationToken cancellationToken);

    // Writes a plain text file (JSON, say) and returns its id: rewritten in place by id, or made in the folder
    // when there is no id or the file has gone. For data an app keeps in Drive to restore from.
    Task<string> WriteTextFile(
        string? fileId, string folderId, string name, string mimeType, string text, CancellationToken cancellationToken);

    // The id of a file this app can see with exactly this name, not trashed, the most recently changed when
    // there are several; null when there is none. How a new phone finds what an old one left in Drive.
    Task<string?> FindFileByName(string name, CancellationToken cancellationToken);

    // A file's whole content as text.
    Task<string> ReadTextFile(string fileId, CancellationToken cancellationToken);
}
