using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Foreword.Infrastructure.Pdf;

internal static class PdfMerger
{
    // Concatenates the pages of two PDFs into one.
    public static byte[] Merge(byte[] first, byte[] second)
    {
        var output = new PdfDocument();
        AppendPages(output, first);
        AppendPages(output, second);

        using var outputStream = new MemoryStream();
        output.Save(outputStream);
        return outputStream.ToArray();
    }

    /// <summary>
    /// How many pages a PDF has, for the contents page — which can only be written once the pages
    /// it describes exist. A contents that guessed its own lengths would be wrong on exactly the
    /// mornings a story ran long.
    /// </summary>
    public static int PageCount(byte[] pdfBytes)
    {
        if (pdfBytes.Length == 0)
        {
            return 0;
        }

        try
        {
            using var stream = new MemoryStream(pdfBytes);
            return PdfReader.Open(stream, PdfDocumentOpenMode.InformationOnly).PageCount;
        }
        catch (Exception)
        {
            // A puzzle PDF that will not open is already a problem the caller handles; a contents
            // line of zero pages is a better answer here than taking the morning down.
            return 0;
        }
    }

    private static void AppendPages(PdfDocument output, byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        for (var i = 0; i < doc.PageCount; i++)
            output.AddPage(doc.Pages[i]);
    }
}
