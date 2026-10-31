using Storyworthtelling.Remarkable;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The tablet's cloud for the read-back, through the shared client that delivery already uses (so the pairing is that
/// phone's own, D85, and no token is read or kept here). It only finds, downloads and trashes: nothing is uploaded, and
/// the cloud's own failures (<see cref="HttpRequestException"/>, a timeout) come out as they went in.
/// </summary>
public sealed class RemarkableAnnotatedPageSource(IRemarkableClient remarkable) : IAnnotatedPageSource
{
    public async Task<TabletDocument?> Download(string title)
    {
        var found = await remarkable.DownloadByTitle(title);
        return found is null ? null : new TabletDocument(found.Id, found.Title, found.PdfBytes);
    }

    public async Task<string?> Locate(string title)
    {
        // The same match the shared client's DownloadByTitle makes, over the listing alone, so the pages are not fetched to find the id.
        var recent = await remarkable.ListRecent();
        return recent.FirstOrDefault(document => string.Equals(document.Title, title, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    public Task Trash(string documentId) => remarkable.MoveToTrash(documentId);
}
