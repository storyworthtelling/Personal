using System.Net;
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
