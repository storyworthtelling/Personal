using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Storyboarder.Infrastructure.External;

// Extracts the text of a PDF via PdfPig (content-stream decoding is not something to hand-roll). Pages are
// concatenated in order with a blank line between them, so downstream chunking and scene detection see the
// document's structure. A scanned (image-only) PDF yields little or no text — that is expected, and the
// review then runs on whatever text is present rather than failing.
public static class PdfTextExtractor
{
    private static readonly Regex Blanks = new(@"[ \t]+", RegexOptions.CultureInvariant);
    private static readonly Regex ExtraBlankLines = new(@"\n{3,}", RegexOptions.CultureInvariant);

    public static string Extract(byte[] pdf)
    {
        if (pdf is not { Length: > 0 })
            return string.Empty;

        using var document = PdfDocument.Open(pdf);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var text = page.Text;
            if (string.IsNullOrWhiteSpace(text))
                continue;
            if (builder.Length > 0)
                builder.Append("\n\n");
            builder.Append(text.Trim());
        }

        var result = builder.ToString().Replace("\r", string.Empty);
        result = Blanks.Replace(result, " ");
        return ExtraBlankLines.Replace(result, "\n\n").Trim();
    }
}
