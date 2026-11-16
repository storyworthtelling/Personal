using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Folio.Domain.Text;

// Extracts the plain manuscript text from a .docx (Office Open XML) file. A .docx is a ZIP whose
// word/document.xml holds the body: paragraphs are <w:p>, text runs <w:t>, with <w:tab>/<w:br> for
// tabs and line breaks. Paragraphs are separated by a blank line so the TextChunker (which splits on
// blank lines) sees the same paragraph structure it gets from a Google Doc. No external dependency —
// the BCL's ZipArchive and XDocument are enough, and the format is stable.
public static class DocxTextExtractor
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace O = "urn:schemas-microsoft-com:office:office";
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

            // Blank-line-separated paragraphs, matching what the chunker expects. Empty paragraphs are
            // dropped so spacing doesn't become a wall of blank lines, except that two or more in a row
            // are the author's scene break (R1) and are kept as two blank lines.
            var builder = new StringBuilder();
            var emptyRun = 0;
            foreach (var paragraph in document.Descendants(W + "p"))
            {
                var text = ParagraphText(paragraph);
                if (text.Length == 0)
                {
                    emptyRun++;
                    continue;
                }
                if (builder.Length > 0)
                    builder.Append(emptyRun >= 2 ? "\n\n\n" : "\n\n");
                builder.Append(text);
                emptyRun = 0;
            }
            return builder.ToString().Trim();
        }
    }

    // Concatenates a paragraph's runs in document order: text nodes as-is, tabs and breaks as their
    // whitespace. Descendants() reaches text nested under hyperlinks, smart tags, and table cells too.
    //
    // A page break (a page-type <w:br>, or a paragraph set to start a new page) and a horizontal rule (an
    // empty bordered paragraph, or a VML rule marked o:hr) are the author's scene breaks (R1), written as
    // a divider line of their own.
    private static string ParagraphText(XElement paragraph)
    {
        var builder = new StringBuilder();
        var properties = paragraph.Element(W + "pPr");
        if (properties?.Element(W + "pageBreakBefore") is { } before && IsOn(before))
            builder.Append(SceneBreak.Divider).Append('\n');

        foreach (var node in paragraph.Descendants())
        {
            if (node.Name == W + "t")
                builder.Append(node.Value);
            else if (node.Name == W + "tab")
                builder.Append('\t');
            else if (node.Name == W + "br" && (string?)node.Attribute(W + "type") == "page")
                builder.Append('\n').Append(SceneBreak.Divider).Append('\n');
            else if (node.Name == W + "br" || node.Name == W + "cr")
                builder.Append('\n');
            else if ((string?)node.Attribute(O + "hr") is "t" or "true")
                builder.Append('\n').Append(SceneBreak.Divider).Append('\n');
        }

        var text = builder.ToString().Trim('\n', ' ', '\t');
        if (text.Length == 0 && properties?.Element(W + "pBdr") is { } border
            && (border.Element(W + "bottom") is not null || border.Element(W + "top") is not null))
            return SceneBreak.Divider;
        return text.TrimEnd();
    }

    private static bool IsOn(XElement toggle) =>
        (string?)toggle.Attribute(W + "val") is null or "1" or "true" or "on";
}
