namespace Storyworthtelling.Remarkable;

// The reMarkable cloud document API, shared across apps. Bidirectional: upload (delivery) plus list /
// download / trash (used by Foreword to locate and retire yesterday's documents).
public interface IRemarkableClient
{
    // Uploads a PDF into a folder (collection) — parentFolderId from EnsureFolder — or at the root when it
    // is null. Refuses (RemarkableRefusedException) a file over RemarkableLimits.MaxBytes before sending, and
    // turns running out of memory into the same refusal, since neither is worth a retry. An app calls this
    // through Storyworthtelling.Paper.IPaperDelivery, never directly (OneRemarkableDoorTests).
    Task Upload(string title, byte[] pdfBytes, string? parentFolderId, CancellationToken cancellationToken);

    // The same upload from a stream of known length, so a large report is sent from where it lies rather than held
    // whole in memory (efficiency audit §18). A client that cannot stream reads it into an array.
    async Task Upload(string title, Stream pdf, long length, string? parentFolderId, CancellationToken cancellationToken)
    {
        if (RemarkableLimits.TooLarge(length) is { } tooLarge)
            throw tooLarge;
        using var copy = new MemoryStream();
        await pdf.CopyToAsync(copy, cancellationToken);
        await Upload(title, copy.ToArray(), parentFolderId, cancellationToken);
    }

    // Finds a folder (collection) with the given name at the account root, creating it if absent, and
    // returns its id — so a caller can deliver into a named folder. Mirrors IDriveArchive.EnsureFolder.
    Task<string> EnsureFolder(string name, CancellationToken cancellationToken);

    // The most recent documents in the account's root, used to locate documents by their (dated) titles.
    Task<IReadOnlyList<RemarkableDoc>> ListRecent();

    // Downloads a document by its exact title, flattened to PDF (annotations included when the cloud can
    // render them). Null when no document with that title exists.
    Task<RemarkableDoc?> DownloadByTitle(string title);

    // Moves a document to the reMarkable trash.
    Task MoveToTrash(string docId);
}
