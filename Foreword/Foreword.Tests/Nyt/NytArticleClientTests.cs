using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Nyt;
using Xunit;

namespace Foreword.Tests.Nyt;

public class NytArticleClientTests
{
    private const string ArticleUrl = "https://www.nytimes.com/2026/04/30/us/test-article.html";

    private static readonly byte[] FakeImageBytes = [137, 80, 78, 71]; // PNG header

    private static NytArticleClient CreateClient(string articleHtml)
    {
        var pageFetcher = new FakePageFetcher(articleHtml);
        var handler = new ImageHandler();
        var httpClient = new HttpClient(handler);
        return new NytArticleClient(pageFetcher, httpClient, NullLogger<NytArticleClient>.Instance);
    }

    [Fact]
    public async Task GetArticle_ReturnsArticleTitle()
    {
        var client = CreateClient(MinimalHtml("Big News Story", []));

        var article = await client.GetArticle(ArticleUrl);

        Assert.Equal("Big News Story", article.Title);
    }

    [Fact]
    public async Task GetArticle_ExtractsParagraphsInOrder()
    {
        var client = CreateClient(MinimalHtml("Title", ["First paragraph.", "Second paragraph."]));

        var article = await client.GetArticle(ArticleUrl);

        var textBlocks = article.Body.OfType<TextBlock>().ToList();
        Assert.Equal(2, textBlocks.Count);
        Assert.Equal("First paragraph.", textBlocks[0].Text);
        Assert.Equal("Second paragraph.", textBlocks[1].Text);
    }

    [Fact]
    public async Task GetArticle_DownloadsInlineImages()
    {
        const string imageUrl = "https://static01.nyt.com/images/2026/04/30/test.jpg";
        var html = HtmlWithImage("Title", imageUrl, "Image caption.");
        var client = CreateClient(html);

        var article = await client.GetArticle(ArticleUrl);

        var imageBlock = article.Body.OfType<ImageBlock>().Single();
        Assert.Equal(FakeImageBytes, imageBlock.ImageBytes);
        Assert.Equal("Image caption.", imageBlock.Caption);
    }

    [Fact]
    public async Task GetArticle_PreservesOrderOfTextAndImages()
    {
        const string imageUrl = "https://static01.nyt.com/images/test.jpg";
        var html = $@"<!DOCTYPE html><html><body>
            <h1>Title</h1>
            <section name=""articleBody"">
                <p>Before image.</p>
                <figure><img src=""{imageUrl}"" /><figcaption>Caption.</figcaption></figure>
                <p>After image.</p>
            </section></body></html>";
        var client = CreateClient(html);

        var article = await client.GetArticle(ArticleUrl);

        Assert.Equal(3, article.Body.Count);
        Assert.IsType<TextBlock>(article.Body[0]);
        Assert.IsType<ImageBlock>(article.Body[1]);
        Assert.IsType<TextBlock>(article.Body[2]);
    }

    [Fact]
    public async Task GetArticle_DoesNotIncludeFigcaptionTextAsParagraph()
    {
        const string imageUrl = "https://static01.nyt.com/images/test.jpg";
        var html = $@"<!DOCTYPE html><html><body>
            <h1>Title</h1>
            <section name=""articleBody"">
                <p>Real paragraph.</p>
                <figure><img src=""{imageUrl}"" /><figcaption>Caption text.</figcaption></figure>
            </section></body></html>";
        var client = CreateClient(html);

        var article = await client.GetArticle(ArticleUrl);

        var textBlocks = article.Body.OfType<TextBlock>().ToList();
        Assert.Single(textBlocks);
        Assert.Equal("Real paragraph.", textBlocks[0].Text);
    }

    [Fact]
    public async Task GetArticle_ReturnsEmptyBodyWhenNoArticleBodySection()
    {
        var html = "<!DOCTYPE html><html><body><h1>Title</h1><p>No article body section here.</p></body></html>";
        var client = CreateClient(html);

        var article = await client.GetArticle(ArticleUrl);

        Assert.Empty(article.Body);
    }

    private static string MinimalHtml(string title, string[] paragraphs)
    {
        var paras = string.Join("\n", paragraphs.Select(p => $"<p>{p}</p>"));
        return $@"<!DOCTYPE html><html><body>
            <h1>{title}</h1>
            <section name=""articleBody""><div>{paras}</div></section>
        </body></html>";
    }

    private static string HtmlWithImage(string title, string imageUrl, string caption) =>
        $@"<!DOCTYPE html><html><body>
            <h1>{title}</h1>
            <section name=""articleBody"">
                <figure>
                    <img src=""{imageUrl}"" />
                    <figcaption>{caption}</figcaption>
                </figure>
            </section></body></html>";

    private sealed class FakePageFetcher(string html) : IArticlePageFetcher
    {
        public Task<string> FetchPageHtmlAsync(string url) => Task.FromResult(html);
    }

    private sealed class ImageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(FakeImageBytes)
            });
    }
}

/// <summary>
/// An article page with no __NEXT_DATA__ script is the ordinary case now: the HTML fallback reads the body. Warning
/// about the missing script on every article, every morning, buried the one warning that matters (no body at all),
/// so the script's absence is a debug line, and it does not name the article.
/// </summary>
public class NytArticleLoggingTests
{
    private const string ArticleUrl = "https://example.com/2026/01/02/world/an-invented-story.html";

    [Fact]
    public async Task APageReadByTheHtmlFallbackWarnsAboutNothing()
    {
        var logger = new ListLogger();
        var client = new NytArticleClient(new FakePageFetcher(@"<!DOCTYPE html><html><body>
            <h1>An invented headline</h1>
            <section name=""articleBody""><p>The first invented paragraph.</p><p>The second invented paragraph.</p></section>
        </body></html>"), new HttpClient(new NoImages()), logger);

        var article = await client.GetArticle(ArticleUrl);

        Assert.Equal(2, article.Body.OfType<TextBlock>().Count());
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("__NEXT_DATA__") && entry.Message.Contains(ArticleUrl));
    }

    [Fact]
    public async Task APageWithNoScriptAndNoBodyWarnsOnce()
    {
        var logger = new ListLogger();
        var client = new NytArticleClient(new FakePageFetcher("<!DOCTYPE html><html><body><h1>An invented headline</h1></body></html>"),
            new HttpClient(new NoImages()), logger);

        var article = await client.GetArticle(ArticleUrl);

        Assert.Empty(article.Body);
        Assert.Single(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    private sealed class FakePageFetcher(string html) : IArticlePageFetcher
    {
        public Task<string> FetchPageHtmlAsync(string url) => Task.FromResult(html);
    }

    private sealed class NoImages : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class ListLogger : ILogger<NytArticleClient>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
