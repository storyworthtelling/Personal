using System.Text;
using Microsoft.Extensions.Logging;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using NytRemarkable.Domain.Interfaces;
using NytRemarkable.Domain.Models;

namespace NytRemarkable.Infrastructure.Pdf;

public class PdfComposer : IPdfComposer
{
    private const double Margin = 54; // 0.75 inch in points

    private readonly ILogger<PdfComposer> logger;

    public PdfComposer(ILogger<PdfComposer> logger)
    {
        this.logger = logger;
    }

    public byte[] Compose(IReadOnlyList<ArticleContent> articles, byte[] crosswordPdfBytes)
    {
        var newsPdfBytes = GenerateNewsPages(articles);
        return MergePdfs(newsPdfBytes, crosswordPdfBytes);
    }

    private byte[] GenerateNewsPages(IReadOnlyList<ArticleContent> articles)
    {
        var doc = new PdfDocument();
        var ctx = new LayoutContext(doc);

        var h1Font    = new XFont("sans-serif", 22, XFontStyleEx.Bold);
        var dateFont  = new XFont("sans-serif", 11, XFontStyleEx.Regular);
        var artFont   = new XFont("sans-serif", 16, XFontStyleEx.Bold);
        var metaFont  = new XFont("sans-serif",  9, XFontStyleEx.Italic);
        var bodyFont  = new XFont("sans-serif", 11, XFontStyleEx.Regular);

        // Header
        DrawCentered(ctx, "The New York Times", h1Font, 28);
        DrawCentered(ctx, DateTime.Today.ToString("MMMM d, yyyy"), dateFont, 18);
        ctx.Gfx.DrawLine(new XPen(XColors.Black, 1.5),
            Margin, ctx.Y, Margin + LayoutContext.ContentWidth, ctx.Y);
        ctx.Advance(14);

        for (var i = 0; i < articles.Count; i++)
        {
            var article = articles[i];

            DrawWrapped(ctx, article.Title ?? string.Empty, artFont);
            ctx.Advance(4);

            var meta = string.Join(" · ",
                new[] { article.Section, article.Byline }.Where(s => !string.IsNullOrEmpty(s)));
            if (!string.IsNullOrEmpty(meta))
            {
                DrawWrapped(ctx, meta, metaFont);
                ctx.Advance(6);
            }

            foreach (var block in article.Body)
            {
                if (block is TextBlock text && !string.IsNullOrWhiteSpace(text?.Text))
                {
                    DrawWrapped(ctx, text.Text, bodyFont);
                    ctx.Advance(4);
                }
                else if (block is ImageBlock image)
                {
                    TryDrawImage(ctx, image, metaFont, logger);
                }
            }

            if (i < articles.Count - 1)
            {
                ctx.Advance(8);
                ctx.NewPageIfNeeded(20);
                ctx.Gfx.DrawLine(new XPen(XColors.LightGray, 0.5),
                    Margin, ctx.Y, Margin + LayoutContext.ContentWidth, ctx.Y);
                ctx.Advance(12);
            }
        }

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static void DrawCentered(LayoutContext ctx, string text, XFont font, double height)
    {
        ctx.NewPageIfNeeded(height);
        ctx.Gfx.DrawString(text, font, XBrushes.Black,
            new XRect(Margin, ctx.Y, LayoutContext.ContentWidth, height),
            XStringFormats.TopCenter);
        ctx.Advance(height);
    }

    private static void DrawWrapped(LayoutContext ctx, string text, XFont font, double lineSpacing = 1.4)
    {
        var lineH = font.GetHeight() * lineSpacing;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();

        foreach (var word in words)
        {
            var test = line.Length > 0 ? $"{line} {word}" : word;
            if (ctx.Gfx.MeasureString(test, font).Width > LayoutContext.ContentWidth && line.Length > 0)
            {
                FlushLine(ctx, line.ToString(), font, lineH);
                line.Clear().Append(word);
            }
            else
            {
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
        }

        if (line.Length > 0)
            FlushLine(ctx, line.ToString(), font, lineH);
    }

    private static void FlushLine(LayoutContext ctx, string line, XFont font, double lineH)
    {
        ctx.NewPageIfNeeded(lineH);
        ctx.Gfx.DrawString(line, font, XBrushes.Black,
            new XRect(Margin, ctx.Y, LayoutContext.ContentWidth, lineH),
            XStringFormats.TopLeft);
        ctx.Advance(lineH);
    }

    private static void TryDrawImage(LayoutContext ctx, ImageBlock image, XFont captionFont, ILogger logger)
    {
        try
        {
            using var stream = new MemoryStream(image.ImageBytes);
            var xImg = XImage.FromStream(stream);
            var imgW = Math.Min(xImg.PointWidth, LayoutContext.ContentWidth);
            var imgH = xImg.PointHeight * imgW / xImg.PointWidth;

            logger.LogInformation("Drawing image {W}x{H}pt from {Bytes} bytes", imgW, imgH, image.ImageBytes.Length);

            ctx.NewPageIfNeeded(imgH);
            ctx.Gfx.DrawImage(xImg, Margin, ctx.Y, imgW, imgH);
            ctx.Advance(imgH + 4);

            if (!string.IsNullOrEmpty(image.Caption))
            {
                DrawWrapped(ctx, image.Caption, captionFont);
                ctx.Advance(4);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Image render failed ({Bytes} bytes): {Error}", image.ImageBytes.Length, ex.Message);
        }
    }

    private static byte[] MergePdfs(byte[] first, byte[] second)
    {
        var output = new PdfDocument();

        using (var stream = new MemoryStream(first))
        {
            var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            for (var i = 0; i < doc.PageCount; i++)
                output.AddPage(doc.Pages[i]);
        }

        using (var stream = new MemoryStream(second))
        {
            var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            for (var i = 0; i < doc.PageCount; i++)
                output.AddPage(doc.Pages[i]);
        }

        using var outputStream = new MemoryStream();
        output.Save(outputStream);
        return outputStream.ToArray();
    }

    private sealed class LayoutContext
    {
        private readonly PdfDocument doc;
        private const double PageWidth = 612;   // US Letter in points
        private const double PageHeight = 792;
        public const double ContentWidth = PageWidth - 2 * Margin;
        private const double BottomY = PageHeight - Margin;

        public XGraphics Gfx { get; private set; }
        public double Y { get; private set; }

        public LayoutContext(PdfDocument doc)
        {
            this.doc = doc;
            Gfx = XGraphics.FromPdfPage(AddLetter());
            Y = Margin;
        }

        public void NewPageIfNeeded(double spaceNeeded)
        {
            if (Y + spaceNeeded > BottomY)
                NewPage();
        }

        public void NewPage()
        {
            Gfx = XGraphics.FromPdfPage(AddLetter());
            Y = Margin;
        }

        public void Advance(double amount) => Y += amount;

        private PdfPage AddLetter()
        {
            var page = doc.AddPage();
            page.Size = PageSize.Letter;
            return page;
        }
    }
}
