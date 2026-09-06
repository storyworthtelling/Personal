using Foreword.Domain.Models;

namespace Foreword.Domain.Interfaces;

public interface IHandwritingExtractor
{
    // Transcribes the hand-written notes in the bottom region of a delivered document
    // into structured notes. The PDF is the annotated document pulled back from reMarkable.
    Task<IReadOnlyList<ParsedNote>> Extract(byte[] annotatedPdf);
}
