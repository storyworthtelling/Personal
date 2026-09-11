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

    public byte[] Compose(IReadOnlyList<ArticleContent> articles, byte[] crosswordPdfBytes) =>
        Compose(articles, crosswordPdfBytes, DateOnly.FromDateTime(DateTime.Today), string.Empty);

    /// <summary>
    /// The whole brief: the foreword page, the stories, the comic, then the puzzle. The comic is
    /// last before the puzzle because that is where it belongs — the thing you read when you have
    /// finished reading.
    /// </summary>
    public byte[] Compose(
        IReadOnlyList<ArticleContent> articles,
        byte[] crosswordPdfBytes,
        DateOnly date,
        string voice,
        IReadOnlyList<Domain.Brief.ComicStrip>? comics = null,
        Domain.Brief.HistoryNote? history = null)
    {
        comics ??= [];
        // The contents can only be written once the pages exist, so the stories are composed first
        // and their page count read back — a contents page that guessed its own lengths would be
        // wrong on exactly the mornings a story ran long.
        var storyPages = PdfMerger.PageCount(ComposeNews(articles));

        var sections = new List<Domain.Brief.BriefSection>
        {
            new("The news", articles.Count(a => !Opinion(a)), storyPages),
            new("Opinion", articles.Count(Opinion), 0),
            new("xkcd", comics.Count, comics.Count),
            new("The crossword", null, PdfMerger.PageCount(crosswordPdfBytes)),
        };

        var newsPdfBytes = ComposeNews(
            articles,
            layout => ForewordPage.Draw(layout, date, voice, sections, history),
            comics,
            crosswordFollows: crosswordPdfBytes.Length > 0);

        return newsPdfBytes.Length == 0
            ? crosswordPdfBytes
            : PdfMerger.Merge(newsPdfBytes, crosswordPdfBytes);
    }

    private static bool Opinion(ArticleContent article) =>
        article.Section.Contains("Opinion", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The news pages (B2). <b>One story per page</b>, full measure, no columns — at reMarkable size
    /// a two-column measure runs to about thirty characters, well under the sixty-odd a reader
    /// wants. Internal so the pages can be read back and checked without merging a puzzle onto them.
    /// </summary>
    internal byte[] ComposeNews(IReadOnlyList<ArticleContent> articles) =>
        ComposeNews(articles, foreword: null, comics: [], crosswordFollows: false);

    /// <summary>
    /// The news pages, optionally opening with the foreword page (B1) and closing with the comic
    /// (B3).
    /// </summary>
    internal byte[] ComposeNews(
        IReadOnlyList<ArticleContent> articles,
        Action<SkiaPageLayout>? foreword,
        IReadOnlyList<Domain.Brief.ComicStrip> comics,
        bool crosswordFollows)
    {
        if (articles.Count == 0 && foreword is null && comics.Count == 0)
        {
            return [];
        }

        using var layout = new SkiaPageLayout();

        var started = false;

        if (foreword is not null)
        {
            foreword(layout);
            started = true;
        }

        // Positions within each section, so the folio can say "1 of 8" — the question a stack of
        // single-story pages otherwise raises, which is how much of the news is left.
        var sections = articles
            .GroupBy(Folio)
            .ToDictionary(group => group.Key, group => group.Count());
        var seen = new Dictionary<string, int>();

        for (var i = 0; i < articles.Count; i++)
        {
            if (started)
            {
                layout.NewPage();
            }

            started = true;

            var article = articles[i];
            var section = Folio(article);
            seen[section] = seen.GetValueOrDefault(section) + 1;

            // Set BEFORE the content, so it lands on this page and on every one the story runs on
            // to — the folio names the STORY's position, and a page two of a story is not a second
            // story. Skia closes a page and it is gone, so there is no stamping one on afterwards.
            var folio = $"{section} · {seen[section]} of {sections[section]}";
            layout.Footer = page => page.DrawText(
                folio, SkiaPageLayout.Sans(PaperTypeScale.Label),
                SkiaPageLayout.Margin, page.BottomY, page.ContentWidth,
                SkiaPageLayout.LineHeight(SkiaPageLayout.Sans(PaperTypeScale.Label)),
                TextAlign.TopLeft, SkiaPageLayout.InkMuted);

            DrawArticle(layout, article);
        }

        // The comic, last. Its own footer, so the folio stops naming the news.
        foreach (var strip in comics)
        {
            if (started)
            {
                layout.NewPage();
            }

            started = true;
            ComicPage.Draw(layout, strip, crosswordFollows);
        }

        return layout.ToPdf();
    }

    private void DrawArticle(SkiaPageLayout layout, ArticleContent article)
    {
        // Roles, not point sizes (D30). Each is the screen ladder at PaperTypeScale.Ratio, so the
        // sheet and the app move together and neither carries a scale of its own.
        var kickerFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var headlineFont = SkiaPageLayout.Serif(PaperTypeScale.Title);
        var bylineFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary);
        var noticeFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary);
        var ledeFont = SkiaPageLayout.Serif(PaperTypeScale.Heading);
        var bodyFont = SkiaPageLayout.Sans(PaperTypeScale.Body);

        layout.DrawWrapped(article.Section.ToUpperInvariant(), kickerFont);
        layout.Advance(4);

        layout.DrawWrapped(article.Title, headlineFont, lineSpacing: 1.15);
        layout.Advance(6);

        if (!string.IsNullOrEmpty(article.Byline))
        {
            layout.DrawWrapped(article.Byline, bylineFont);
            layout.Advance(4);
        }

        // Bug B1, on the page. One plain line: no decoration, no margin symbol, nothing to decode.
        // Without it a two-sentence page looks like a short story rather than a failed download, and
        // the reader has no way to tell the difference.
        if (article.AbstractOnly)
        {
            layout.DrawWrapped("Abstract only — the full article did not fetch.", noticeFont);
            layout.Advance(4);
        }

        layout.Advance(8);

        // The opening paragraph is set apart, larger, the way a lede is — it is the paragraph that
        // decides whether the rest gets read.
        var first = true;
        foreach (var block in article.Body)
        {
            if (block is TextBlock text && !string.IsNullOrWhiteSpace(text.Text))
            {
                layout.DrawWrapped(text.Text, first ? ledeFont : bodyFont);
                layout.Advance(first ? 10 : 6);
                first = false;
            }
            else if (block is ImageBlock image)
            {
                TryDrawImage(layout, image, bylineFont);
            }
        }

    }

    /// <summary>
    /// What the folio calls this story's section. Opinion is named; everything else is the news,
    /// because "World · 1 of 1" beside "Business · 1 of 1" counts sections rather than stories and
    /// tells the reader nothing about how much is left.
    /// </summary>
    private static string Folio(ArticleContent article) =>
        article.Section.Contains("Opinion", StringComparison.OrdinalIgnoreCase) ? "Opinion" : "The news";

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
