using System.IO.Compression;
using System.Text;
using Storyboarder.Domain.Text;
using Xunit;

namespace Storyboarder.Tests.Text;

public class EpubTextExtractorTests
{
    private static byte[] BuildEpub(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }
        return stream.ToArray();
    }

    [Fact]
    public void ExtractsSpineTextInReadingOrder_StrippingMarkup()
    {
        var container =
            "<?xml version=\"1.0\"?><container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
            "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/>" +
            "</rootfiles></container>";
        var opf =
            "<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\">" +
            "<manifest>" +
            "<item id=\"c2\" href=\"ch2.xhtml\" media-type=\"application/xhtml+xml\"/>" +
            "<item id=\"c1\" href=\"ch1.xhtml\" media-type=\"application/xhtml+xml\"/>" +
            "</manifest>" +
            "<spine><itemref idref=\"c1\"/><itemref idref=\"c2\"/></spine>" +
            "</package>";
        var ch1 = "<html><head><title>1</title></head><body><h1>Chapter One</h1>" +
                  "<p>The knight rode to the castle.</p></body></html>";
        var ch2 = "<html><body><p>At the gate, he knocked &amp; waited.</p></body></html>";

        var epub = BuildEpub(
            ("mimetype", "application/epub+zip"),
            ("META-INF/container.xml", container),
            ("OEBPS/content.opf", opf),
            ("OEBPS/ch1.xhtml", ch1),
            ("OEBPS/ch2.xhtml", ch2));

        var text = EpubTextExtractor.Extract(epub);

        // Reading order follows the SPINE (ch1 then ch2), not the manifest order.
        var firstIndex = text.IndexOf("The knight rode", StringComparison.Ordinal);
        var secondIndex = text.IndexOf("At the gate", StringComparison.Ordinal);
        Assert.True(firstIndex >= 0 && secondIndex > firstIndex, "expected spine reading order");
        // Markup is stripped and entities decoded.
        Assert.DoesNotContain("<p>", text);
        Assert.Contains("knocked & waited.", text);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, EpubTextExtractor.Extract([]));
    }
}
