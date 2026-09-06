namespace Storyworthtelling.Remarkable;

// The reMarkable cloud document API, shared across apps. Bidirectional: upload (delivery) plus list /
// download / trash (used by Foreword to locate and retire yesterday's documents).
public interface IRemarkableClient
{
    // Uploads a PDF as a document at the account root (the original behavior).
    Task Upload(string title, byte[] pdfBytes);

    // Uploads a PDF into a folder (collection) — parentFolderId from EnsureFolder — or at the root when it
    // is null. A separate overload from the root one so existing callers and their mocks are unaffected.
    Task Upload(string title, byte[] pdfBytes, string? parentFolderId);

    // Finds a folder (collection) with the given name at the account root, creating it if absent, and
    // returns its id — so a caller can deliver into a named folder. Mirrors IDriveArchive.EnsureFolder.
    Task<string> EnsureFolder(string name);

    // The most recent documents in the account's root, used to locate documents by their (dated) titles.
    Task<IReadOnlyList<RemarkableDoc>> ListRecent();

    // Downloads a document by its exact title, flattened to PDF (annotations included when the cloud can
    // render them). Null when no document with that title exists.
    Task<RemarkableDoc?> DownloadByTitle(string title);

    // Moves a document to the reMarkable trash.
    Task MoveToTrash(string docId);
}
