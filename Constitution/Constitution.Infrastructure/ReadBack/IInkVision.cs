using Constitution.Domain.ReadBack;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The vision read: given a document and the cloud's PDF of it, say for each box it was asked about whether
/// there is ink, what was written, and how sure it is. It sends the page and the list of boxes and nothing
/// else (no name and no Body reading beside the page, which itself prints the day's plan), and a reply it cannot make sense of comes back as pages that were
/// not read (<see cref="InkDocumentRead"/>), not as an exception and never as empty pages.
/// </summary>
public interface IInkVision
{
    Task<InkDocumentRead> Read(ExpectedDocument document, byte[] annotatedPdf, CancellationToken cancellation);
}
