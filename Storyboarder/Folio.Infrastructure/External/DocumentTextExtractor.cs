using System.Text;
using Folio.Domain.Text;
using Google.Apis.Docs.v1.Data;

namespace Folio.Infrastructure.External;

// Flattens a Google Docs document into plain text by concatenating the text runs of each paragraph.
// Kept separate from the network fetch so it can be unit-tested against a hand-built Document.
public static class DocumentTextExtractor
{
    public static string Extract(Document document)
    {
        var builder = new StringBuilder();
        if (document.Body?.Content is null)
            return string.Empty;

        foreach (var element in document.Body.Content)
        {
            if (element.Paragraph?.Elements is null)
                continue;

            foreach (var paragraphElement in element.Paragraph.Elements)
            {
                // A horizontal rule or a page break carries no text; it is the author's scene break (R1),
                // written as a divider line of its own.
                if (paragraphElement.HorizontalRule is not null || paragraphElement.PageBreak is not null)
                {
                    if (builder.Length > 0 && builder[^1] != '\n')
                        builder.Append('\n');
                    builder.Append(SceneBreak.Divider).Append('\n');
                    continue;
                }

                var run = paragraphElement.TextRun?.Content;
                if (!string.IsNullOrEmpty(run))
                    builder.Append(run);
            }
        }

        return builder.ToString();
    }
}
