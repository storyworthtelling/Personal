using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Storyboarder.Domain.Text;

// Extracts the plain manuscript text from a .docx (Office Open XML) file. A .docx is a ZIP whose
// word/document.xml holds the body: paragraphs are <w:p>, text runs <w:t>, with <w:tab>/<w:br> for
// tabs and line breaks. Paragraphs are separated by a blank line so the TextChunker (which splits on
// blank lines) sees the same paragraph structure it gets from a Google Doc. No external dependency —
// the BCL's ZipArchive and XDocument are enough, and the format is stable.
public static class DocxTextExtractor
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string DocumentEntry = "word/document.xml";

    public static string Extract(byte[] content)
    {
        if (content is null || content.Length == 0)
            throw new ArgumentException("The .docx content is empty.", nameof(content));

        using var stream = new MemoryStream(content, writable: false);
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("The file is not a valid .docx (Office Open XML) document.", ex);
        }

        using (archive)
        {
            var entry = archive.GetEntry(DocumentEntry)
                        ?? throw new InvalidDataException($"The .docx is missing its {DocumentEntry} — it may be corrupt or not a Word document.");

            using var entryStream = entry.Open();
            var document = XDocument.Load(entryStream);

            var paragraphs = document
                .Descendants(W + "p")
                .Select(ParagraphText);

            // Blank-line-separated paragraphs, matching what the chunker expects; drop empties so a run
            // of spacing paragraphs doesn't become a wall of blank lines.
            var text = string.Join("\n\n", paragraphs.Where(p => p.Length > 0));
            return text.Trim();
        }
    }

    // Concatenates a paragraph's runs in document order: text nodes as-is, tabs and breaks as their
    // whitespace. Descendants() reaches text nested under hyperlinks, smart tags, and table cells too.
    private static string ParagraphText(XElement paragraph)
    {
        var builder = new StringBuilder();
        foreach (var node in paragraph.Descendants())
        {
            if (node.Name == W + "t")
                builder.Append(node.Value);
            else if (node.Name == W + "tab")
                builder.Append('\t');
            else if (node.Name == W + "br" || node.Name == W + "cr")
                builder.Append('\n');
        }
        return builder.ToString().TrimEnd();
    }
}
