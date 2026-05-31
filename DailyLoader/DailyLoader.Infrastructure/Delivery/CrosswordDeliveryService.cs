using Microsoft.Extensions.Logging;
using DailyLoader.Domain.Interfaces;
using DailyLoader.Domain.Models;

namespace DailyLoader.Infrastructure.Delivery;

public class CrosswordDeliveryService(
    INytCrosswordClient nytClient,
    IRemarkableClient remarkableClient,
    INytTopStoriesClient topStoriesClient,
    INytArticleClient articleClient,
    IPdfComposer pdfComposer,
    ILogger<CrosswordDeliveryService> logger)
{
    // Returns a human-readable summary of what was delivered, e.g. "8 articles + crossword"
    // or "crossword only — <reason>" so callers can surface it to the user.
    public async Task<string> DeliverDailyPuzzle(DateOnly date)
    {
        logger.LogInformation("Downloading daily crossword for {Date}", date);
        var crosswordBytes = await nytClient.DownloadDailyPuzzle(date);

        var (finalBytes, newsStatus) = await TryComposeWithNews(crosswordBytes, date);

        var title = $"NYT Crossword {date:yyyy-MM-dd}";
        logger.LogInformation("Uploading to reMarkable: {Title} ({NewsStatus})", title, newsStatus);
        await remarkableClient.Upload(title, finalBytes);

        return newsStatus;
    }

    public async Task DeliverBonusPuzzle(DateOnly date)
    {
        logger.LogInformation("Downloading bonus crossword for {Date}", date);
        var pdfBytes = await nytClient.DownloadBonusPuzzle(date);

        var title = $"NYT Bonus Crossword {date:MMMM yyyy}";
        logger.LogInformation("Uploading to reMarkable: {Title}", title);
        await remarkableClient.Upload(title, pdfBytes);
    }

    public static bool IsFirstSundayOfMonth(DateOnly date) =>
        date.DayOfWeek == DayOfWeek.Sunday && date.Day <= 7;

    private async Task<(byte[] bytes, string status)> TryComposeWithNews(byte[] crosswordBytes, DateOnly date)
    {
        List<TopStory> allStories;
        try
        {
            var newsTask = topStoriesClient.GetTopNewsStories(5);
            var opinionTask = topStoriesClient.GetTopOpinionArticles(3);
            await Task.WhenAll(newsTask, opinionTask);
            allStories = newsTask.Result.Concat(opinionTask.Result).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Top Stories API failed for {Date}", date);
            return (crosswordBytes, $"crossword only — news API error: {ex.Message}");
        }

        if (allStories.Count == 0)
            return (crosswordBytes, "crossword only — no stories returned by API");

        var articles = new List<ArticleContent>();
        foreach (var story in allStories)
        {
            try
            {
                var article = await articleClient.GetArticle(story.Url);
                logger.LogInformation("[FULL ARTICLE] {Title} — {Blocks} block(s)", article.Title, article.Body.Count);
                articles.Add(article);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ABSTRACT FALLBACK] {Title}: {Error}", story.Title, ex.Message);
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
            return (crosswordBytes, "crossword only — all articles failed and abstracts were empty");

        try
        {
            logger.LogInformation("Composing {Count} articles with crossword for {Date}", articles.Count, date);
            var composed = pdfComposer.Compose(articles, crosswordBytes);
            return (composed, $"{articles.Count} article(s) + crossword");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDF composition failed for {Date}", date);
            return (crosswordBytes, $"crossword only — PDF composition error: {ex.Message}");
        }
    }
}
