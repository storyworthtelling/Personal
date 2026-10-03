using Storyworthtelling.Handwriting;

namespace Index.Tests.Support;

/// <summary>A fake <see cref="IHandwritingExtractor"/> that returns a fixed, seeded set of notes and
/// records the instruction string and PDF bytes it was called with — used to prove
/// <see cref="WorksheetReadBackInstruction"/>'s coordinates reach the extractor unchanged, and to feed
/// deterministic read-back results without a real Claude call.</summary>
public sealed class FakeHandwritingExtractor : IHandwritingExtractor
{
    public IReadOnlyList<ParsedNote> Notes { get; set; } = [];
    public List<string> InstructionsReceived { get; } = [];
    public List<byte[]> PdfBytesReceived { get; } = [];

    public Task<IReadOnlyList<ParsedNote>> Extract(byte[] annotatedPdf)
    {
        PdfBytesReceived.Add(annotatedPdf);
        return Task.FromResult(Notes);
    }

    public void RecordInstruction(string instruction) => InstructionsReceived.Add(instruction);
}
