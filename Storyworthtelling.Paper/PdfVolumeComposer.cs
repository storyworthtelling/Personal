using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Storyworthtelling.Paper;

// Assembles ordered composed-page images into a single volume PDF, one page per image, drawn full
// page. Draws images only (no text), so no font resolver is required.
public sealed class PdfVolumeComposer : IVolumePdfComposer
{
    private const double PointsPerInch = 72.0;

    public byte[] Compose(IReadOnlyList<byte[]> pageImages, int dpi)
    {
        using var document = new PdfDocument();
        var images = new List<XImage>();
        var streams = new List<MemoryStream>();
        try
        {
            foreach (var pageImage in pageImages)
            {
                // PdfSharp's image importer calls MemoryStream.GetBuffer(), which is only allowed on
                // an expandable stream — so write the bytes in rather than wrapping the array.
                var stream = new MemoryStream();
                stream.Write(pageImage, 0, pageImage.Length);
                stream.Position = 0;
                streams.Add(stream);
                var image = XImage.FromStream(stream);
                images.Add(image);
                DrawFullPage(document, image, dpi);
            }

            if (document.PageCount == 0)
                document.AddPage();

            using var output = new MemoryStream();
            document.Save(output);
            return output.ToArray();
        }
        finally
        {
            foreach (var image in images)
                image.Dispose();
            foreach (var stream in streams)
                stream.Dispose();
        }
    }

    public byte[] AppendPage(byte[]? existingPdf, byte[] pageImage, int dpi)
    {
        // Reopen the growing volume PDF and append just this one page, so only a single page raster is
        // held in memory at a time — unlike Compose, which must keep every page's image open until Save.
        using var pdfStream = new MemoryStream();
        if (existingPdf is { Length: > 0 })
            pdfStream.Write(existingPdf, 0, existingPdf.Length);
        pdfStream.Position = 0;

        using var document = existingPdf is { Length: > 0 }
            ? PdfReader.Open(pdfStream, PdfDocumentOpenMode.Modify)
            : new PdfDocument();

        // PdfSharp's image importer calls MemoryStream.GetBuffer(), which is only allowed on an
        // expandable stream — so write the bytes in rather than wrapping the array.
        using var imageStream = new MemoryStream();
        imageStream.Write(pageImage, 0, pageImage.Length);
        imageStream.Position = 0;
        using var image = XImage.FromStream(imageStream);
        DrawFullPage(document, image, dpi);

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    // Adds one full-bleed page sized from the raster's pixels at the requested DPI, rather than trusting
    // the PNG's own resolution metadata (SkiaSharp writes none, so PdfSharp would assume 96 DPI and
    // oversize the page). The image fills the page.
    private static void DrawFullPage(PdfDocument document, XImage image, int dpi)
    {
        double pointWidth = image.PixelWidth * PointsPerInch / dpi;
        double pointHeight = image.PixelHeight * PointsPerInch / dpi;
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(pointWidth);
        page.Height = XUnit.FromPoint(pointHeight);
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawImage(image, 0, 0, pointWidth, pointHeight);
    }
}
