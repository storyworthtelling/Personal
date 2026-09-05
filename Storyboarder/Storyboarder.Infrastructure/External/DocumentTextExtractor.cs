using System.Text;
using Google.Apis.Docs.v1.Data;

namespace Storyboarder.Infrastructure.External;

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
                var run = paragraphElement.TextRun?.Content;
                if (!string.IsNullOrEmpty(run))
                    builder.Append(run);
            }
        }

        return builder.ToString();
    }
}
