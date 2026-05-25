using Microsoft.Extensions.Logging;
using NytRemarkable.Domain.Interfaces;
using NytRemarkable.Domain.Models;

namespace NytRemarkable.Infrastructure.Delivery;

public class CrosswordDeliveryService(
    INytCrosswordClient nytClient,
    IRemarkableClient remarkableClient,
    INytTopStoriesClient topStoriesClient,
    INytArticleClient articleClient,
    IPdfComposer pdfComposer,
    ILogger<CrosswordDeliveryService> logger)
{
    public async Task DeliverDailyPuzzle(DateOnly date)
    {
        logger.LogInformation("Downloading daily crossword for {Date}", date);
        var crosswordBytes = await nytClient.DownloadDailyPuzzle(date);

        var finalBytes = await TryComposeWithNews(crosswordBytes, date);

        var title = $"NYT Crossword {date:yyyy-MM-dd}";
        logger.LogInformation("Uploading to reMarkable: {Title}", title);
        await remarkableClient.Upload(title, finalBytes);

        logger.LogInformation("Delivered: {Title}", title);
    }

    public async Task DeliverBonusPuzzle(DateOnly date)
    {
        logger.LogInformation("Downloading bonus crossword for {Date}", date);
        var pdfBytes = await nytClient.DownloadBonusPuzzle(date);

        var title = $"NYT Bonus Crossword {date:MMMM yyyy}";
        logger.LogInformation("Uploading to reMarkable: {Title}", title);
        await remarkableClient.Upload(title, pdfBytes);

        logger.LogInformation("Delivered: {Title}", title);
    }

    public static bool IsFirstSundayOfMonth(DateOnly date) =>
        date.DayOfWeek == DayOfWeek.Sunday && date.Day <= 7;

    private async Task<byte[]> TryComposeWithNews(byte[] crosswordBytes, DateOnly date)
    {
        try
        {
            var newsTask = topStoriesClient.GetTopNewsStories(5);
            var opinionTask = topStoriesClient.GetTopOpinionArticles(3);
            await Task.WhenAll(newsTask, opinionTask);

            var allStories = newsTask.Result.Concat(opinionTask.Result).ToList();
            if (allStories.Count == 0)
                return crosswordBytes;

            var articles = new List<ArticleContent>();
            foreach (var story in allStories)
            {
                if (articles.Count > 0)
                    await Task.Delay(TimeSpan.FromMinutes(1));

                try
                {
                    var article = await articleClient.GetArticle(story.Url);
                    logger.LogInformation("[FULL ARTICLE] {Title} — {Blocks} block(s)", article.Title, article.Body.Count);
                    articles.Add(article);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[ABSTRACT FALLBACK] {Title} ({Url}): {Error}",
                        story.Title, story.Url, ex.Message);
                    if (!string.IsNullOrEmpty(story.Abstract))
                        articles.Add(new ArticleContent
                        {
                            Title = story.Title,
                            Byline = story.Byline,
                            Section = story.Section,
                            Body = [new TextBlock { Text = story.Abstract }]
                        });
                }
            }

            if (articles.Count == 0)
                return crosswordBytes;

            logger.LogInformation("Composing {Count} articles with crossword for {Date}", articles.Count, date);
            return pdfComposer.Compose(articles, crosswordBytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch news for {Date}, delivering crossword only", date);
            return crosswordBytes;
        }
    }
}
