using Google.Apis.Docs.v1.Data;
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
