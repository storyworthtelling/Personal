using Google.Apis.Docs.v1.Data;
using Folio.Domain.Text;
using Folio.Infrastructure.External;
using Xunit;

namespace Folio.Tests.External;

public class DocumentTextExtractorTests
{
    private static Document DocumentWith(params string[][] paragraphs)
    {
        var content = paragraphs.Select(runs => new StructuralElement
        {
            Paragraph = new Paragraph
            {
                Elements = runs.Select(run => new ParagraphElement { TextRun = new TextRun { Content = run } }).ToList()
            }
        }).ToList();

        return new Document { Body = new Body { Content = content } };
    }

    [Fact]
    public void Extract_ConcatenatesTextRunsAcrossParagraphs()
    {
        var document = DocumentWith(
            ["Chapter One\n"],
            ["It was ", "a dark ", "night.\n"]);

        var text = DocumentTextExtractor.Extract(document);

        Assert.Equal("Chapter One\nIt was a dark night.\n", text);
    }

    /// <summary>
    /// A horizontal rule and a page break are the author's own scene breaks (owner's ruling, R1). The
    /// Docs API gives them as their own elements with no text, so they used to vanish.
    /// </summary>
    [Fact]
    public void Extract_WritesAHorizontalRuleAndAPageBreakAsADividerLine()
    {
        var document = new Document
        {
            Body = new Body
            {
                Content =
                [
                    Para(new ParagraphElement { TextRun = new TextRun { Content = "Scene one.\n" } }),
                    Para(new ParagraphElement { HorizontalRule = new HorizontalRule() },
                        new ParagraphElement { TextRun = new TextRun { Content = "\n" } }),
                    Para(new ParagraphElement { TextRun = new TextRun { Content = "Scene two.\n" } }),
                    Para(new ParagraphElement { PageBreak = new PageBreak() },
                        new ParagraphElement { TextRun = new TextRun { Content = "\n" } }),
                    Para(new ParagraphElement { TextRun = new TextRun { Content = "Scene three.\n" } }),
                ],
            },
        };

        var text = DocumentTextExtractor.Extract(document);

        Assert.Equal(3, new SceneDetector().Detect(text, []).Count);
        Assert.Contains("\n" + SceneBreak.Divider + "\n", text, StringComparison.Ordinal);
    }

    private static StructuralElement Para(params ParagraphElement[] elements) =>
        new() { Paragraph = new Paragraph { Elements = [.. elements] } };

    [Fact]
    public void Extract_EmptyDocument_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, DocumentTextExtractor.Extract(new Document()));
    }

    [Fact]
    public void Extract_IgnoresNonParagraphElements()
    {
        var document = new Document
        {
            Body = new Body
            {
                Content =
                [
                    new StructuralElement { SectionBreak = new SectionBreak() },
                    new StructuralElement
                    {
                        Paragraph = new Paragraph
                        {
                            Elements = [new ParagraphElement { TextRun = new TextRun { Content = "Real text." } }]
                        }
                    }
                ]
            }
        };

        Assert.Equal("Real text.", DocumentTextExtractor.Extract(document));
    }
}
