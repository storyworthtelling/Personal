using Folio.Domain.Text;
using Xunit;

namespace Folio.Tests.Text;

public class RtfTextExtractorTests
{
    [Fact]
    public void ExtractsPlainText_DroppingControlWordsAndFontTable()
    {
        var rtf =
            @"{\rtf1\ansi\deff0{\fonttbl{\f0 Times New Roman;}}" +
            @"\f0\fs24 The knight rode to the castle.\par " +
            @"At the gate, he knocked.\par}";

        var text = RtfTextExtractor.Extract(rtf);

        Assert.Contains("The knight rode to the castle.", text);
        Assert.Contains("At the gate, he knocked.", text);
        // The font-table destination is not body text.
        Assert.DoesNotContain("Times New Roman", text);
        // Paragraphs are separated so downstream chunking/scene detection sees the structure.
        Assert.Contains("\n\n", text);
    }

    [Fact]
    public void DecodesHexAndUnicodeEscapes()
    {
        // \'e9 is é in Latin-1; \u233? is é as a Unicode escape with an ASCII fallback char to skip.
        var rtf = @"{\rtf1 caf\'e9 and na\u239?ve.\par}";

        var text = RtfTextExtractor.Extract(rtf);

        Assert.Contains("café", text);
        Assert.Contains("naïve.", text);
    }

    [Fact]
    public void EscapedBracesAndBackslashesAreLiteral()
    {
        var rtf = @"{\rtf1 A \{brace\} and a \\ slash.\par}";

        var text = RtfTextExtractor.Extract(rtf);

        Assert.Contains("A {brace} and a \\ slash.", text);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, RtfTextExtractor.Extract(string.Empty));
    }
}
