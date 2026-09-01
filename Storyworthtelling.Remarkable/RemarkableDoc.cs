namespace Storyworthtelling.Remarkable;

// A document retrieved from the reMarkable cloud, flattened to PDF (annotations included
// when the cloud can render them).
public class RemarkableDoc
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required byte[] PdfBytes { get; init; }
}
