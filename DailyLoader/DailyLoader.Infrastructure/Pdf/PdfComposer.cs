using Microsoft.Extensions.Logging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using DailyLoader.Domain.Interfaces;
using DailyLoader.Domain.Models;

namespace DailyLoader.Infrastructure.Pdf;

public class PdfComposer : IPdfComposer
{
    private readonly ILogger<PdfComposer> logger;

    public PdfComposer(ILogger<PdfComposer> logger)
    {
        this.logger = logger;
    }

    public byte[] Compose(IReadOnlyList<ArticleContent> articles, byte[] crosswordPdfBytes)
    {
        var newsPdfBytes = GenerateNewsPages(articles);
        return PdfMerger.Merge(newsPdfBytes, crosswordPdfBytes);
    }

    private byte[] GenerateNewsPages(IReadOnlyList<ArticleContent> articles)
    {
        SystemFontResolver.Register();

        var doc = new PdfDocument();
        var layout = new PdfLayout(doc);

        var h1Font    = new XFont("sans-serif", 22, XFontStyleEx.Bold);
        var dateFont  = new XFont("sans-serif", 11, XFontStyleEx.Regular);
        var artFont   = new XFont("sans-serif", 16, XFontStyleEx.Bold);
        var metaFont  = new XFont("sans-serif",  9, XFontStyleEx.Italic);
        var bodyFont  = new XFont("sans-serif", 11, XFontStyleEx.Regular);

        // Header
        layout.DrawCentered("The New York Times", h1Font, 28);
        layout.DrawCentered(DateTime.Today.ToString("MMMM d, yyyy"), dateFont, 18);
        layout.Gfx.DrawLine(new XPen(XColors.Black, 1.5),
            PdfLayout.Margin, layout.Y, PdfLayout.Margin + layout.ContentWidth, layout.Y);
        layout.Advance(14);

        for (var i = 0; i < articles.Count; i++)
        {
            var article = articles[i];

            layout.DrawWrapped(article.Title ?? string.Empty, artFont);
            layout.Advance(4);

            var meta = string.Join(" · ",
                new[] { article.Section, article.Byline }.Where(s => !string.IsNullOrEmpty(s)));
            if (!string.IsNullOrEmpty(meta))
            {
                layout.DrawWrapped(meta, metaFont);
                layout.Advance(6);
            }

            foreach (var block in article.Body)
            {
                if (block is TextBlock text && !string.IsNullOrWhiteSpace(text?.Text))
                {
                    layout.DrawWrapped(text.Text, bodyFont);
                    layout.Advance(4);
                }
                else if (block is ImageBlock image)
                {
                    TryDrawImage(layout, image, metaFont);
                }
            }

            if (i < articles.Count - 1)
            {
                layout.Advance(8);
                layout.NewPageIfNeeded(20);
                layout.Gfx.DrawLine(new XPen(XColors.LightGray, 0.5),
                    PdfLayout.Margin, layout.Y, PdfLayout.Margin + layout.ContentWidth, layout.Y);
                layout.Advance(12);
            }
        }

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private void TryDrawImage(PdfLayout layout, ImageBlock image, XFont captionFont)
    {
        try
        {
            using var stream = new MemoryStream(image.ImageBytes, 0, image.ImageBytes.Length, false, true);
            var xImg = XImage.FromStream(stream);
            var imgW = Math.Min(xImg.PointWidth, layout.ContentWidth);
            var imgH = xImg.PointHeight * imgW / xImg.PointWidth;

            logger.LogInformation("Drawing image {W}x{H}pt from {Bytes} bytes", imgW, imgH, image.ImageBytes.Length);

            layout.NewPageIfNeeded(imgH);
            layout.Gfx.DrawImage(xImg, PdfLayout.Margin, layout.Y, imgW, imgH);
            layout.Advance(imgH + 4);

            if (!string.IsNullOrEmpty(image.Caption))
            {
                layout.DrawWrapped(image.Caption, captionFont);
                layout.Advance(4);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Image render failed ({Bytes} bytes): {Error}", image.ImageBytes.Length, ex.Message);
        }
    }
}
