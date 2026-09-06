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

    private static void AppendPages(PdfDocument output, byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        for (var i = 0; i < doc.PageCount; i++)
            output.AddPage(doc.Pages[i]);
    }
}
