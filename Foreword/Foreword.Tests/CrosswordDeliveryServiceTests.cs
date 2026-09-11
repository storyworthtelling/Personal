using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Foreword.Domain.Brief;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Delivery;
using Storyworthtelling.Remarkable;
using Xunit;

namespace Foreword.Tests;

public class CrosswordDeliveryServiceTests
{
    private readonly Mock<INytCrosswordClient> nytClient = new();
    private readonly Mock<IRemarkableClient> remarkableClient = new();
    private readonly Mock<INytTopStoriesClient> topStoriesClient = new();
    private readonly Mock<INytArticleClient> articleClient = new();
    private readonly Mock<IPdfComposer> pdfComposer = new();
    private readonly Mock<IComicClient> comics = new();
    private readonly Mock<IHistoryClient> history = new();
    private readonly FakePreferences preferences = new();

    private static readonly TopStory FakeNewsStory = new()
    {
        Title = "News", Byline = "By A", Section = "U.S.", Url = "https://nytimes.com/news",
        Abstract = "News summary."
    };
    private static readonly TopStory FakeOpinionStory = new()
    {
        Title = "Opinion", Byline = "By B", Section = "Opinion", Url = "https://nytimes.com/opinion",
        Abstract = "Opinion summary."
    };
    // A body with words in it. An ArticleContent with an EMPTY body now falls back to the abstract
    // — a fetch that succeeds and returns nothing usable reads on the page exactly like a story
    // with no words in it — so a fixture with no body would exercise the fallback rather than the
    // full-article path it was written for. See ...FallsBackWhenTheFetchReturnsNoText below.
    private static readonly ArticleContent FakeArticle = new()
    {
        Title = "News", Byline = "By A", Section = "U.S.",
        Body = [new TextBlock { Text = "The full text of the story." }]
    };

    public CrosswordDeliveryServiceTests()
    {
        // Neither extra is set up by default, so every existing test exercises the brief WITHOUT a
        // comic or a factoid — which is the morning where they could not be reached, and the one
        // that must still deliver.
        comics.Setup(c => c.Latest()).ReturnsAsync((int?)null);
        history.Setup(h => h.OnThisDay(It.IsAny<DateOnly>())).ReturnsAsync([]);
    }

    private CrosswordDeliveryService CreateService() =>
        new(nytClient.Object, remarkableClient.Object, topStoriesClient.Object,
            articleClient.Object, pdfComposer.Object, preferences, comics.Object, history.Object,
            NullLogger<CrosswordDeliveryService>.Instance);

    /// <summary>The Sources screen's values, at their defaults.</summary>
    private sealed class FakePreferences : IBriefPreferences
    {
        public int NewsCount { get; set; } = 5;
        public int OpinionCount { get; set; } = 3;
        public IReadOnlyList<Feed> Feeds { get; set; } = [];
        public int SeenComic { get; set; }
        public ReadingTaste Taste { get; set; } = ReadingTaste.Empty;

        public void FeedsAfterAMorning(IReadOnlyCollection<string> failed, DateOnly today)
        {
        }
    }

    [Fact]
    public async Task DeliverDailyPuzzle_DownloadsPuzzleForGivenDate()
    {
        var date = new DateOnly(2025, 4, 14);
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync([1, 2, 3]);
        topStoriesClient.Setup(c => c.GetTopNewsStories(5)).ReturnsAsync([]);
        topStoriesClient.Setup(c => c.GetTopOpinionArticles(3)).ReturnsAsync([]);
        pdfComposer.Setup(c => c.Compose(
            It.IsAny<IReadOnlyList<ArticleContent>>(), It.IsAny<byte[]>(), It.IsAny<DateOnly>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ComicStrip>?>(), It.IsAny<HistoryNote?>()))
            .Returns([9]);

        await CreateService().DeliverDailyPuzzle(date);

        nytClient.Verify(c => c.DownloadDailyPuzzle(date), Times.Once);
    }

    [Fact]
    public async Task DeliverDailyPuzzle_FetchesTopNewsAndOpinionStories()
    {
        var date = new DateOnly(2025, 4, 14);
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync([1]);
        topStoriesClient.Setup(c => c.GetTopNewsStories(5)).ReturnsAsync([]);
        topStoriesClient.Setup(c => c.GetTopOpinionArticles(3)).ReturnsAsync([]);
        pdfComposer.Setup(c => c.Compose(
            It.IsAny<IReadOnlyList<ArticleContent>>(), It.IsAny<byte[]>(), It.IsAny<DateOnly>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ComicStrip>?>(), It.IsAny<HistoryNote?>()))
            .Returns([9]);

        await CreateService().DeliverDailyPuzzle(date);

        topStoriesClient.Verify(c => c.GetTopNewsStories(5), Times.Once);
        topStoriesClient.Verify(c => c.GetTopOpinionArticles(3), Times.Once);
    }

    [Fact]
    public async Task DeliverDailyPuzzle_FetchesFullArticleForEachStory()
    {
        var date = new DateOnly(2025, 4, 14);
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync([1]);
        topStoriesClient.Setup(c => c.GetTopNewsStories(5)).ReturnsAsync([FakeNewsStory]);
        topStoriesClient.Setup(c => c.GetTopOpinionArticles(3)).ReturnsAsync([FakeOpinionStory]);
        articleClient.Setup(c => c.GetArticle(It.IsAny<string>())).ReturnsAsync(FakeArticle);
        pdfComposer.Setup(c => c.Compose(
            It.IsAny<IReadOnlyList<ArticleContent>>(), It.IsAny<byte[]>(), It.IsAny<DateOnly>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ComicStrip>?>(), It.IsAny<HistoryNote?>()))
            .Returns([9]);

        await CreateService().DeliverDailyPuzzle(date);

        articleClient.Verify(c => c.GetArticle(FakeNewsStory.Url), Times.Once);
        articleClient.Verify(c => c.GetArticle(FakeOpinionStory.Url), Times.Once);
    }

    [Fact]
    public async Task DeliverDailyPuzzle_UploadsPdfComposedFromArticlesAndCrossword()
    {
        var date = new DateOnly(2025, 4, 14);
        var crosswordBytes = new byte[] { 1, 2, 3 };
        var composedBytes = new byte[] { 9, 8, 7 };
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync(crosswordBytes);
        topStoriesClient.Setup(c => c.GetTopNewsStories(5)).ReturnsAsync([FakeNewsStory]);
        topStoriesClient.Setup(c => c.GetTopOpinionArticles(3)).ReturnsAsync([]);
        articleClient.Setup(c => c.GetArticle(FakeNewsStory.Url)).ReturnsAsync(FakeArticle);
        pdfComposer.Setup(c => c.Compose(
                It.IsAny<IReadOnlyList<ArticleContent>>(), crosswordBytes, It.IsAny<DateOnly>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ComicStrip>?>(), It.IsAny<HistoryNote?>()))
            .Returns(composedBytes);

        await CreateService().DeliverDailyPuzzle(date);

        remarkableClient.Verify(c => c.Upload(It.IsAny<string>(), composedBytes), Times.Once);
    }

    [Fact]
    public async Task DeliverDailyPuzzle_UploadsTitleContainingDate()
    {
        var date = new DateOnly(2025, 4, 14);
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync([1]);
        topStoriesClient.Setup(c => c.GetTopNewsStories(5)).ReturnsAsync([]);
        topStoriesClient.Setup(c => c.GetTopOpinionArticles(3)).ReturnsAsync([]);
        pdfComposer.Setup(c => c.Compose(
            It.IsAny<IReadOnlyList<ArticleContent>>(), It.IsAny<byte[]>(), It.IsAny<DateOnly>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ComicStrip>?>(), It.IsAny<HistoryNote?>()))
            .Returns([9]);

        await CreateService().DeliverDailyPuzzle(date);

        remarkableClient.Verify(c => c.Upload(
            It.Is<string>(t => t.Contains("2025-04-14")),
            It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public async Task DeliverDailyPuzzle_UploadsTitleNamedMorningBriefNotCrossword()
    {
        var date = new DateOnly(2025, 4, 14);
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync([1]);
        topStoriesClient.Setup(c => c.GetTopNewsStories(5)).ReturnsAsync([]);
        topStoriesClient.Setup(c => c.GetTopOpinionArticles(3)).ReturnsAsync([]);
        pdfComposer.Setup(c => c.Compose(
            It.IsAny<IReadOnlyList<ArticleContent>>(), It.IsAny<byte[]>(), It.IsAny<DateOnly>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ComicStrip>?>(), It.IsAny<HistoryNote?>()))
            .Returns([9]);

        await CreateService().DeliverDailyPuzzle(date);

        remarkableClient.Verify(c => c.Upload(
            It.Is<string>(t => t.Contains("Morning Brief") && !t.Contains("Crossword")),
            It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public async Task DeliverBonusPuzzle_UploadsTitleWithoutCrosswordWording()
    {
        var date = new DateOnly(2025, 6, 1);
        nytClient.Setup(c => c.DownloadBonusPuzzle(date)).ReturnsAsync([1]);

        await CreateService().DeliverBonusPuzzle(date);

        remarkableClient.Verify(c => c.Upload(
            It.Is<string>(t => !t.Contains("Crossword")),
            It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public async Task DeliverDailyPuzzle_StillDeliversCrosswordWhenNewsFetchFails()
    {
        var date = new DateOnly(2025, 4, 14);
        var crosswordBytes = new byte[] { 1, 2, 3 };
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync(crosswordBytes);
        topStoriesClient.Setup(c => c.GetTopNewsStories(5)).ThrowsAsync(new HttpRequestException("down"));
        topStoriesClient.Setup(c => c.GetTopOpinionArticles(3)).ThrowsAsync(new HttpRequestException("down"));

        await CreateService().DeliverDailyPuzzle(date);

        remarkableClient.Verify(c => c.Upload(It.IsAny<string>(), crosswordBytes), Times.Once);
        pdfComposer.Verify(c => c.Compose(It.IsAny<IReadOnlyList<ArticleContent>>(), It.IsAny<byte[]>(), It.IsAny<DateOnly>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ComicStrip>?>(), It.IsAny<HistoryNote?>()), Times.Never);
    }

    /// <summary>
    /// A fetch can SUCCEED and return nothing usable — an empty body reads on the page exactly like
    /// a story with no words in it, which is the same failure wearing a success code. It falls back
    /// to the abstract and is marked, like any other failed fetch (bug B1).
    /// </summary>
    [Fact]
    public async Task DeliverDailyPuzzle_FallsBackWhenTheFetchReturnsNoText()
    {
        var date = new DateOnly(2025, 4, 14);
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync([1]);
        topStoriesClient.Setup(c => c.GetTopNewsStories(5)).ReturnsAsync([FakeNewsStory]);
        topStoriesClient.Setup(c => c.GetTopOpinionArticles(3)).ReturnsAsync([]);
        articleClient.Setup(c => c.GetArticle(FakeNewsStory.Url)).ReturnsAsync(
            new ArticleContent { Title = "News", Byline = "By A", Section = "U.S.", Body = [] });
        pdfComposer.Setup(c => c.Compose(
                It.IsAny<IReadOnlyList<ArticleContent>>(), It.IsAny<byte[]>(), It.IsAny<DateOnly>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ComicStrip>?>(), It.IsAny<HistoryNote?>()))
            .Returns([9]);

        await CreateService().DeliverDailyPuzzle(date);

        pdfComposer.Verify(c => c.Compose(
            It.Is<IReadOnlyList<ArticleContent>>(list => list.Single().AbstractOnly),
            It.IsAny<byte[]>(), It.IsAny<DateOnly>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<ComicStrip>?>(), It.IsAny<HistoryNote?>()), Times.Once);
    }

    [Fact]
    public async Task DeliverDailyPuzzle_UsesAbstractWhenArticleFetchFails()
    {
        var date = new DateOnly(2025, 4, 14);
        var crosswordBytes = new byte[] { 1, 2, 3 };
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync(crosswordBytes);
        topStoriesClient.Setup(c => c.GetTopNewsStories(5)).ReturnsAsync([FakeNewsStory]);
        topStoriesClient.Setup(c => c.GetTopOpinionArticles(3)).ReturnsAsync([FakeOpinionStory]);
        articleClient.Setup(c => c.GetArticle(FakeNewsStory.Url)).ThrowsAsync(new HttpRequestException("403"));
        articleClient.Setup(c => c.GetArticle(FakeOpinionStory.Url)).ReturnsAsync(FakeArticle);
        pdfComposer.Setup(c => c.Compose(It.IsAny<IReadOnlyList<ArticleContent>>(), crosswordBytes)).Returns([9]);

        await CreateService().DeliverDailyPuzzle(date);

        pdfComposer.Verify(c => c.Compose(
            It.Is<IReadOnlyList<ArticleContent>>(list =>
                list.Count == 2 &&
                list[0].Title == FakeNewsStory.Title &&
                list[0].Body.Count == 1 &&
                ((TextBlock)list[0].Body[0]).Text == FakeNewsStory.Abstract),
            crosswordBytes, It.IsAny<DateOnly>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<ComicStrip>?>(), It.IsAny<HistoryNote?>()), Times.Once);
    }

    [Fact]
    public async Task DeliverBonusPuzzle_DownloadsBonusPuzzleForGivenDate()
    {
        var date = new DateOnly(2025, 6, 1);
        nytClient.Setup(c => c.DownloadBonusPuzzle(date)).ReturnsAsync([1]);

        await CreateService().DeliverBonusPuzzle(date);

        nytClient.Verify(c => c.DownloadBonusPuzzle(date), Times.Once);
    }

    [Fact]
    public async Task DeliverBonusPuzzle_UploadsPdfWithMonthAndYearInTitle()
    {
        var date = new DateOnly(2025, 6, 1);
        nytClient.Setup(c => c.DownloadBonusPuzzle(date)).ReturnsAsync([1]);

        await CreateService().DeliverBonusPuzzle(date);

        remarkableClient.Verify(c => c.Upload(
            It.Is<string>(t => t.Contains("June") && t.Contains("2025")),
            It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public async Task DeliverBonusPuzzle_DoesNotFetchNews()
    {
        var date = new DateOnly(2025, 6, 1);
        nytClient.Setup(c => c.DownloadBonusPuzzle(date)).ReturnsAsync([1]);

        await CreateService().DeliverBonusPuzzle(date);

        topStoriesClient.Verify(c => c.GetTopNewsStories(It.IsAny<int>()), Times.Never);
        topStoriesClient.Verify(c => c.GetTopOpinionArticles(It.IsAny<int>()), Times.Never);
    }
}
