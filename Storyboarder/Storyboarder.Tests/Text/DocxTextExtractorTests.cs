using System.IO.Compression;
using System.Text;
using Storyboarder.Domain.Text;
using Xunit;

namespace Storyboarder.Tests.Text;

public class DocxTextExtractorTests
{
    [Fact]
    public void Extracts_Paragraphs_SeparatedByBlankLines_JoiningRunsWithinAParagraph()
    {
        var docx = BuildDocx(
            "<w:p><w:r><w:t>First paragraph.</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t>Second </w:t></w:r><w:r><w:t>paragraph.</w:t></w:r></w:p>");

        var text = DocxTextExtractor.Extract(docx);

        Assert.Equal("First paragraph.\n\nSecond paragraph.", text);
    }

    [Fact]
    public void Renders_TabsAndLineBreaks_WithinAParagraph()
    {
        var docx = BuildDocx(
            "<w:p><w:r><w:t>Left</w:t><w:tab/><w:t>Right</w:t><w:br/><w:t>Next line</w:t></w:r></w:p>");

        var text = DocxTextExtractor.Extract(docx);

        Assert.Equal("Left\tRight\nNext line", text);
    }

    [Fact]
    public void Skips_EmptyParagraphs_SoSpacingDoesntBecomeWallsOfBlankLines()
    {
        var docx = BuildDocx(
            "<w:p><w:r><w:t>One.</w:t></w:r></w:p>" +
            "<w:p></w:p>" +
            "<w:p><w:r><w:t>Two.</w:t></w:r></w:p>");

        Assert.Equal("One.\n\nTwo.", DocxTextExtractor.Extract(docx));
    }

    [Fact]
    public void Reaches_TextNestedInTablesAndHyperlinks()
    {
        var docx = BuildDocx(
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>Cell text</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            "<w:p><w:hyperlink><w:r><w:t>Linked words</w:t></w:r></w:hyperlink></w:p>");

        var text = DocxTextExtractor.Extract(docx);

        Assert.Contains("Cell text", text);
        Assert.Contains("Linked words", text);
    }

    [Fact]
    public void EmptyContent_Throws()
    {
        Assert.Throws<ArgumentException>(() => DocxTextExtractor.Extract([]));
    }

    [Fact]
    public void NonZipBytes_ThrowWithAClearMessage()
    {
        var ex = Assert.Throws<InvalidDataException>(() => DocxTextExtractor.Extract(Encoding.UTF8.GetBytes("not a zip")));
        Assert.Contains(".docx", ex.Message);
    }

    [Fact]
    public void ZipWithoutDocumentXml_Throws()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/other.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<x/>");
        }
        Assert.Throws<InvalidDataException>(() => DocxTextExtractor.Extract(buffer.ToArray()));
    }

    // Wraps a body fragment into a minimal but valid .docx (a ZIP with word/document.xml).
    private static byte[] BuildDocx(string bodyXml)
    {
        const string ns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var documentXml =
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"{ns}\"><w:body>{bodyXml}</w:body></w:document>";

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(documentXml);
        }
        return buffer.ToArray();
    }
}
