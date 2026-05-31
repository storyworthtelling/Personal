using DailyLoader.Domain.Models;

namespace DailyLoader.Domain.Interfaces;

public interface IRemarkableClient
{
    Task Upload(string title, byte[] pdfBytes);

    // The most recent documents in the account's root, used to locate yesterday's
    // calendar and checklist by their dated titles.
    Task<IReadOnlyList<RemarkableDoc>> ListRecent();

    // Downloads a document by its exact title, flattened to PDF (annotations included
    // when the cloud can render them). Null when no document with that title exists.
    Task<RemarkableDoc?> DownloadByTitle(string title);

    // Moves a document to the reMarkable trash.
    Task MoveToTrash(string docId);
}
