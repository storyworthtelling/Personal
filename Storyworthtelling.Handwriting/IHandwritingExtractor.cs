namespace Storyworthtelling.Handwriting;

// Transcribes the hand-written ink in a region of a PDF page into structured notes. Which region,
// and what each field of a note means, is entirely the caller's own instruction text — this
// interface only promises the shape that comes back.
public interface IHandwritingExtractor
{
    Task<IReadOnlyList<ParsedNote>> Extract(byte[] annotatedPdf);
}
