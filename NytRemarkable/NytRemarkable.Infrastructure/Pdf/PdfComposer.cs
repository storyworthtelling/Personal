using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using NytRemarkable.Domain.Interfaces;
using NytRemarkable.Domain.Models;

namespace NytRemarkable.Infrastructure.Pdf;

public class PdfComposer : IPdfComposer
{
    public PdfComposer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Compose(IReadOnlyList<ArticleContent> articles, byte[] crosswordPdfBytes)
    {
        var newsPdfBytes = GenerateNewsPages(articles);
        return MergePdfs(newsPdfBytes, crosswordPdfBytes);
    }

    private static byte[] GenerateNewsPages(IReadOnlyList<ArticleContent> articles)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.75f, Unit.Inch);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(11));

                page.Content().Column(col =>
                {
                    col.Item().Text("The New York Times")
                        .Bold().FontSize(22).AlignCenter();
                    col.Item().PaddingBottom(4)
                        .Text(DateTime.Today.ToString("MMMM d, yyyy"))
                        .FontSize(11).AlignCenter();
                    col.Item().PaddingBottom(16).LineHorizontal(1.5f);

                    for (var i = 0; i < articles.Count; i++)
                    {
                        RenderArticle(col, articles[i]);

                        if (i < articles.Count - 1)
                        {
                            col.Item().PaddingVertical(12).LineHorizontal(0.5f);
                        }
                    }
                });
            });
        }).GeneratePdf();
    }

    private static void RenderArticle(ColumnDescriptor col, ArticleContent article)
    {
        col.Item().Text(article.Title).Bold().FontSize(16);

        if (!string.IsNullOrEmpty(article.Byline) || !string.IsNullOrEmpty(article.Section))
        {
            var meta = string.Join(" · ", new[] { article.Section, article.Byline }
                .Where(s => !string.IsNullOrEmpty(s)));
            col.Item().PaddingBottom(6).Text(meta).Italic().FontSize(9).FontColor(Colors.Grey.Darken2);
        }

        foreach (var block in article.Body)
        {
            if (block is TextBlock text)
            {
                col.Item().PaddingBottom(4).Text(text.Text).FontSize(11);
            }
            else if (block is ImageBlock image)
            {
                col.Item().PaddingVertical(6).MaxWidth(400).Image(image.ImageBytes);
                if (!string.IsNullOrEmpty(image.Caption))
                    col.Item().PaddingBottom(6).Text(image.Caption).Italic().FontSize(9)
                        .FontColor(Colors.Grey.Darken2);
            }
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
}
