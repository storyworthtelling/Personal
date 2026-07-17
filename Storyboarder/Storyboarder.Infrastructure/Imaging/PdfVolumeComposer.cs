using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Storyboarder.Domain.Imaging;

namespace Storyboarder.Infrastructure.Imaging;

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

                // Size the page from the raster's pixels at the requested DPI, rather than trusting the
                // PNG's own resolution metadata (SkiaSharp writes none, so PdfSharp would assume 96 DPI
                // and oversize the page). Full-bleed: the image fills the page.
                double pointWidth = image.PixelWidth * PointsPerInch / dpi;
                double pointHeight = image.PixelHeight * PointsPerInch / dpi;
                var page = document.AddPage();
                page.Width = XUnit.FromPoint(pointWidth);
                page.Height = XUnit.FromPoint(pointHeight);
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(image, 0, 0, pointWidth, pointHeight);
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
}
