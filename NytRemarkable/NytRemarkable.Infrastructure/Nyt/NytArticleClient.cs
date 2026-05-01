using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Options;
using NytRemarkable.Domain.Interfaces;
using NytRemarkable.Domain.Models;

namespace NytRemarkable.Infrastructure.Nyt;

public class NytArticleClient : INytArticleClient
{
    private readonly HttpClient httpClient;
    private readonly NytOptions options;

    public NytArticleClient(HttpClient httpClient, IOptions<NytOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<ArticleContent> GetArticle(string url)
    {
        var html = await FetchHtml(url);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html));

        var title = document.QuerySelector("h1")?.TextContent.Trim() ?? string.Empty;
        var articleBody = document.QuerySelector("section[name='articleBody']");

        var body = articleBody != null
            ? await ExtractBody(articleBody)
            : [];

        return new ArticleContent
        {
            Title = title,
            Byline = ExtractByline(document),
            Section = ExtractSection(document),
            Body = body
        };
    }

    private async Task<string> FetchHtml(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"NYT-S={options.SessionCookie}");
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36");

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"NYT article fetch failed: {(int)response.StatusCode} ({response.StatusCode}) for {url}");

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<IReadOnlyList<ContentBlock>> ExtractBody(IElement articleBody)
    {
        var blocks = new List<ContentBlock>();
        await WalkElement(articleBody, blocks);
        return blocks;
    }

    private async Task WalkElement(IElement element, List<ContentBlock> blocks)
    {
        foreach (var child in element.Children)
        {
            if (child.LocalName == "figcaption")
                continue;

            if (child.LocalName == "p")
            {
                var text = child.TextContent.Trim();
                if (!string.IsNullOrEmpty(text))
                    blocks.Add(new TextBlock { Text = text });
            }
            else if (child.LocalName == "figure")
            {
                var imageBlock = await TryExtractImage(child);
                if (imageBlock != null)
                    blocks.Add(imageBlock);
            }
            else
            {
                await WalkElement(child, blocks);
            }
        }
    }

    private async Task<ImageBlock?> TryExtractImage(IElement figure)
    {
        var img = figure.QuerySelector("img");
        var src = img?.GetAttribute("src");
        if (string.IsNullOrEmpty(src) || src.StartsWith("data:"))
            return null;

        var caption = figure.QuerySelector("figcaption")?.TextContent.Trim();

        try
        {
            var imageBytes = await httpClient.GetByteArrayAsync(src);
            return new ImageBlock { ImageBytes = imageBytes, Caption = caption };
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractByline(IDocument document)
    {
        return document.QuerySelector("[itemprop='name']")?.TextContent.Trim()
            ?? document.QuerySelector(".byline")?.TextContent.Trim()
            ?? string.Empty;
    }

    private static string ExtractSection(IDocument document)
    {
        return document.QuerySelector("meta[property='article:section']")
            ?.GetAttribute("content")?.Trim()
            ?? string.Empty;
    }
}
