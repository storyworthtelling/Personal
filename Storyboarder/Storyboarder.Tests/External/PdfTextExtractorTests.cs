using Storyboarder.Infrastructure.External;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Storyboarder.Tests.External;

public class PdfTextExtractorTests
{
    private static byte[] BuildPdf(params string[] pageLines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var line in pageLines)
        {
            var page = builder.AddPage(595, 842);
            page.AddText(line, 12, new PdfPoint(50, 700), font);
        }
        return builder.Build();
    }

    [Fact]
    public void ExtractsTextFromEveryPage_InOrder()
    {
        var pdf = BuildPdf("The knight rode to the castle.", "At the gate, he knocked.");

        var text = PdfTextExtractor.Extract(pdf);

        Assert.Contains("The knight rode to the castle.", text);
        Assert.Contains("At the gate, he knocked.", text);
        var first = text.IndexOf("knight", StringComparison.Ordinal);
        var second = text.IndexOf("gate", StringComparison.Ordinal);
        Assert.True(first >= 0 && second > first, "expected pages concatenated in order");
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PdfTextExtractor.Extract([]));
    }
}
