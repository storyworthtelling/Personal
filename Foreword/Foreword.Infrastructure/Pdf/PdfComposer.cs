using Microsoft.Extensions.Logging;
using SkiaSharp;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;

using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

// Renders the morning news pages with SkiaSharp (via SkiaPageLayout), then concatenates the external
// crossword PDF after them with PdfMerger.
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
        using var layout = new SkiaPageLayout();

        // Roles, not point sizes (D30). Each is the screen ladder at PaperTypeScale.Ratio, so the
        // sheet and the app move together and neither carries a scale of its own.
        var h1Font = SkiaPageLayout.Sans(PaperTypeScale.Title, bold: true);
        var dateFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary);
        var artFont = SkiaPageLayout.Sans(PaperTypeScale.Heading, bold: true);
        var metaFont = SkiaPageLayout.Sans(PaperTypeScale.Label);   // was italic; no embedded sans italic
        var bodyFont = SkiaPageLayout.Sans(PaperTypeScale.Body);

        // Header
        layout.DrawCentered("The New York Times", h1Font, 28);
        layout.DrawCentered(DateTime.Today.ToString("MMMM d, yyyy"), dateFont, 18);
        layout.DrawLine(SkiaPageLayout.Margin, layout.Y,
            SkiaPageLayout.Margin + layout.ContentWidth, layout.Y, SkiaPageLayout.Ink, 1.5f);
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
                layout.DrawLine(SkiaPageLayout.Margin, layout.Y,
                    SkiaPageLayout.Margin + layout.ContentWidth, layout.Y, SkiaPageLayout.Hairline, 0.5f);
                layout.Advance(12);
            }
        }

        return layout.ToPdf();
    }

    private void TryDrawImage(SkiaPageLayout layout, ImageBlock image, SKFont captionFont)
    {
        try
        {
            var (natW, natH) = SkiaPageLayout.ImageSizePoints(image.ImageBytes);
            if (natW <= 0 || natH <= 0)
                return;

            var imgW = Math.Min(natW, layout.ContentWidth);
            var imgH = natH * imgW / natW;

            logger.LogInformation("Drawing image {W}x{H}pt from {Bytes} bytes", imgW, imgH, image.ImageBytes.Length);

            layout.NewPageIfNeeded(imgH);
            layout.DrawImage(image.ImageBytes, SkiaPageLayout.Margin, layout.Y, imgW, imgH);
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
