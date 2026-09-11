using Microsoft.Extensions.Logging;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Brief;
using Foreword.Domain.Models;
using Storyworthtelling.Remarkable;

namespace Foreword.Infrastructure.Delivery;

public class CrosswordDeliveryService(
    INytCrosswordClient nytClient,
    IRemarkableClient remarkableClient,
    INytTopStoriesClient topStoriesClient,
    INytArticleClient articleClient,
    IPdfComposer pdfComposer,
    IBriefPreferences preferences,
    IComicClient comics,
    IHistoryClient history,
    ILogger<CrosswordDeliveryService> logger)
{
    // Returns a human-readable summary of what was delivered, e.g. "8 articles + puzzle"
    // or "puzzle only — <reason>" so callers can surface it to the user.
    public async Task<string> DeliverDailyPuzzle(DateOnly date)
    {
        logger.LogInformation("Downloading daily crossword for {Date}", date);
        var crosswordBytes = await nytClient.DownloadDailyPuzzle(date);

        var (finalBytes, newsStatus) = await TryComposeWithNews(crosswordBytes, date);

        // The delivered document is the morning brief (news + puzzle), not "the crossword".
        var title = $"Morning Brief {date:yyyy-MM-dd}";
        logger.LogInformation("Uploading to reMarkable: {Title} ({NewsStatus})", title, newsStatus);
        await remarkableClient.Upload(title, finalBytes);

        return newsStatus;
    }

    public async Task DeliverBonusPuzzle(DateOnly date)
    {
        logger.LogInformation("Downloading bonus crossword for {Date}", date);
        var pdfBytes = await nytClient.DownloadBonusPuzzle(date);

        var title = $"Bonus Puzzle — {date:MMMM yyyy}";
        logger.LogInformation("Uploading to reMarkable: {Title}", title);
        await remarkableClient.Upload(title, pdfBytes);
    }

    /// <summary>
    /// The strips the owner has not seen. Everything OFFERED is marked seen, not just what fitted —
    /// otherwise the same backlog is offered tomorrow and the cap becomes a queue that never drains.
    /// </summary>
    private async Task<IReadOnlyList<ComicStrip>> ReadComics()
    {
        if (await comics.Latest() is not { } latest)
        {
            return [];
        }

        var seen = preferences.SeenComic;
        var wanted = ComicSelection.Unseen(latest, seen == 0 ? [] : [seen], max: 3);

        var strips = new List<ComicStrip>();
        foreach (var number in wanted)
        {
            if (await comics.Get(number) is { } strip)
            {
                strips.Add(strip);
            }
        }

        preferences.SeenComic = ComicSelection.HighWaterMark(latest, wanted);
        return strips;
    }

    private async Task<HistoryNote?> ReadHistory(DateOnly date) =>
        HistoryNoteChoice.Pick(await history.OnThisDay(date), date);

    public static bool IsFirstSundayOfMonth(DateOnly date) =>
        date.DayOfWeek == DayOfWeek.Sunday && date.Day <= 7;

    private async Task<(byte[] bytes, string status)> TryComposeWithNews(byte[] crosswordBytes, DateOnly date)
    {
        List<TopStory> allStories;
        try
        {
            // The counts the owner set on the Sources screen, not constants in here.
            var newsTask = topStoriesClient.GetTopNewsStories(preferences.NewsCount);
            var opinionTask = topStoriesClient.GetTopOpinionArticles(preferences.OpinionCount);
            await Task.WhenAll(newsTask, opinionTask);
            allStories = newsTask.Result.Concat(opinionTask.Result).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Top Stories API failed for {Date}", date);
            return (crosswordBytes, $"puzzle only — news API error: {ex.Message}");
        }

        if (allStories.Count == 0)
            return (crosswordBytes, "puzzle only — no stories returned by API");

        // Deduplicated and ordered before anything is fetched: a story that appears under Home and
        // again under its own section was being fetched twice AND printed twice, so the owner
        // counted eight stories and found seven.
        // Deduplicated and ordered, then RANKED by taste — which moves a story up or down and never
        // drops one (D29). A profile that filtered would quietly narrow the brief to what the owner
        // already agrees with, and they asked for one story they would disagree with.
        var stories = ReadingTaste.Rank(BriefSelection.Choose(allStories), preferences.Taste);

        var articles = new List<ArticleContent>();
        foreach (var story in stories)
        {
            try
            {
                var article = await articleClient.GetArticle(story.Url);
                logger.LogInformation("[FULL ARTICLE] {Title} — {Blocks} block(s)", article.Title, article.Body.Count);

                // The fetcher can succeed and return nothing usable, which reads on the page exactly
                // like a story with no words in it. That is the abstract's case too.
                if (article.Body.OfType<TextBlock>().Any(b => !string.IsNullOrWhiteSpace(b.Text)))
                {
                    articles.Add(article);
                    continue;
                }

                logger.LogWarning("[EMPTY ARTICLE] {Title} fetched with no text", story.Title);
                Fallback(story);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ABSTRACT FALLBACK] {Title}: {Error}", story.Title, ex.Message);
                Fallback(story);
            }
        }

        // The abstract standing in for a story that did not fetch — MARKED, so the page can say so
        // (bug B1). A story with no abstract either is dropped rather than printed as a headline
        // over white space, which is a fault printed at full size rather than a story.
        void Fallback(TopStory story)
        {
            if (BriefSelection.FromAbstract(story) is { } stub)
            {
                articles.Add(stub);
            }
        }

        if (articles.Count == 0)
            return (crosswordBytes, "puzzle only — all articles failed and abstracts were empty");

        try
        {
            logger.LogInformation("Composing {Count} articles with crossword for {Date}", articles.Count, date);

            var strips = await ReadComics();
            var factoid = await ReadHistory(date);

            var composed = pdfComposer.Compose(
                articles, crosswordBytes, date, voice: string.Empty, strips, factoid);

            // The status sentence counts the ones that fell back, so a morning of failed fetches is
            // visible on the Settings screen rather than only in a log nobody opens.
            var stubs = articles.Count(article => article.AbstractOnly);
            var note = stubs == 0 ? string.Empty : $" ({stubs} abstract-only)";
            return (composed, $"{articles.Count} article(s){note} + puzzle");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDF composition failed for {Date}", date);
            return (crosswordBytes, $"crossword only — PDF composition error: {ex.Message}");
        }
    }
}
