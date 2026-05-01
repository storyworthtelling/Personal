using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NytRemarkable.Domain.Models;
using NytRemarkable.Infrastructure.Nyt;
using Xunit;

namespace NytRemarkable.Tests.Nyt;

public class NytArticleClientTests
{
    private const string ArticleUrl = "https://www.nytimes.com/2026/04/30/us/test-article.html";

    private static readonly byte[] FakeImageBytes = [137, 80, 78, 71]; // PNG header

    private static NytArticleClient CreateClient(
        string articleHtml,
        out List<HttpRequestMessage> capturedRequests)
    {
        var requests = new List<HttpRequestMessage>();
        capturedRequests = requests;

        var handler = new RoutingHandler(req =>
        {
            requests.Add(req);
            if (req.RequestUri!.Host.Contains("nytimes.com"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(articleHtml, Encoding.UTF8, "text/html")
                };
            // Image CDN
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(FakeImageBytes)
            };
        });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new NytOptions { SessionCookie = "test-cookie" });
        return new NytArticleClient(httpClient, options, NullLogger<NytArticleClient>.Instance);
    }

    [Fact]
    public async Task GetArticle_FetchesArticleUrlWithSessionCookie()
    {
        var client = CreateClient(MinimalHtml("Title", []), out var requests);

        await client.GetArticle(ArticleUrl);

        var articleRequest = requests.First(r => r.RequestUri!.ToString() == ArticleUrl);
        Assert.True(articleRequest.Headers.TryGetValues("Cookie", out var values));
        Assert.Contains("NYT-S=test-cookie", values);
    }

    [Fact]
    public async Task GetArticle_ReturnsArticleTitle()
    {
        var client = CreateClient(MinimalHtml("Big News Story", []), out _);

        var article = await client.GetArticle(ArticleUrl);

        Assert.Equal("Big News Story", article.Title);
    }

    [Fact]
    public async Task GetArticle_ExtractsParagraphsInOrder()
    {
        var client = CreateClient(MinimalHtml("Title", ["First paragraph.", "Second paragraph."]), out _);

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
        var client = CreateClient(html, out _);

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
        var client = CreateClient(html, out _);

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
        var client = CreateClient(html, out _);

        var article = await client.GetArticle(ArticleUrl);

        var textBlocks = article.Body.OfType<TextBlock>().ToList();
        Assert.Single(textBlocks);
        Assert.Equal("Real paragraph.", textBlocks[0].Text);
    }

    [Fact]
    public async Task GetArticle_ReturnsEmptyBodyWhenNoArticleBodySection()
    {
        var html = "<!DOCTYPE html><html><body><h1>Title</h1><p>No article body section here.</p></body></html>";
        var client = CreateClient(html, out _);

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

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
