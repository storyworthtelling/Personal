using System.Text.Json;
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

        var body = await ExtractArticleBody(document);

        return new ArticleContent
        {
            Title = title,
            Byline = ExtractByline(document),
            Section = ExtractSection(document),
            Body = body
        };
    }

    private async Task<IReadOnlyList<ContentBlock>> ExtractArticleBody(IDocument document)
    {
        // Try structured __NEXT_DATA__ JSON first — most reliable
        var blocks = await TryParseNextData(document);
        if (blocks.Count > 0)
            return blocks;

        // Fall back to HTML selectors
        var articleBody =
            document.QuerySelector("section[name='articleBody']") ??
            document.QuerySelector("[name='articleBody']") ??
            document.QuerySelector("[data-testid='article-body']") ??
            document.QuerySelector("article");

        return articleBody != null ? await WalkElement(articleBody) : [];
    }

    // NYT embeds full article JSON in a __NEXT_DATA__ script tag
    private async Task<IReadOnlyList<ContentBlock>> TryParseNextData(IDocument document)
    {
        var script = document.QuerySelector("script#__NEXT_DATA__");
        if (script == null) return [];

        try
        {
            using var doc = JsonDocument.Parse(script.TextContent);
            var bodyArray = FindBodyArray(doc.RootElement);
            if (bodyArray == null) return [];

            return await ParseBodyBlocks(bodyArray.Value);
        }
        catch
        {
            return [];
        }
    }

    // Recursively search the JSON tree for an array of article body blocks
    private static JsonElement? FindBodyArray(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array && IsBodyArray(element))
            return element;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var found = FindBodyArray(prop.Value);
                if (found != null) return found;
            }
        }

        return null;
    }

    private static bool IsBodyArray(JsonElement array)
    {
        // A body array has objects with __typename of Paragraph, Image, etc.
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return false;
            if (!item.TryGetProperty("__typename", out var tn)) return false;
            var name = tn.GetString();
            if (name is "Paragraph" or "Image" or "Heading" or "DetailBlock")
                return true;
        }
        return false;
    }

    private async Task<IReadOnlyList<ContentBlock>> ParseBodyBlocks(JsonElement bodyArray)
    {
        var blocks = new List<ContentBlock>();

        foreach (var block in bodyArray.EnumerateArray())
        {
            if (!block.TryGetProperty("__typename", out var tn)) continue;
            var typeName = tn.GetString();

            if (typeName is "Paragraph" or "Heading" or "DetailBlock")
            {
                var text = ExtractTextFromBlock(block);
                if (!string.IsNullOrWhiteSpace(text))
                    blocks.Add(new TextBlock { Text = text });
            }
            else if (typeName is "Image" or "GridImage")
            {
                var imageBlock = await TryExtractNextDataImage(block);
                if (imageBlock != null)
                    blocks.Add(imageBlock);
            }
        }

        return blocks;
    }

    private static string ExtractTextFromBlock(JsonElement block)
    {
        // Paragraphs have a "content" array of inline text nodes
        if (!block.TryGetProperty("content", out var content)) return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var node in content.EnumerateArray())
        {
            if (node.TryGetProperty("text", out var text))
                sb.Append(text.GetString());
        }
        return sb.ToString().Trim();
    }

    private async Task<ImageBlock?> TryExtractNextDataImage(JsonElement block)
    {
        // Navigate: crops[0].renditions[0].url OR media.url
        string? imageUrl = null;

        if (block.TryGetProperty("crops", out var crops))
        {
            var firstCrop = crops.EnumerateArray().FirstOrDefault();
            if (firstCrop.ValueKind == JsonValueKind.Object &&
                firstCrop.TryGetProperty("renditions", out var renditions))
            {
                var bestRendition = renditions.EnumerateArray()
                    .LastOrDefault(); // last is typically largest
                if (bestRendition.ValueKind == JsonValueKind.Object &&
                    bestRendition.TryGetProperty("url", out var urlEl))
                    imageUrl = urlEl.GetString();
            }
        }

        if (imageUrl == null) return null;

        string? caption = null;
        if (block.TryGetProperty("caption", out var cap) &&
            cap.TryGetProperty("content", out var capContent))
            caption = ExtractTextFromBlock(cap);

        try
        {
            var imageBytes = await httpClient.GetByteArrayAsync(imageUrl);
            return new ImageBlock { ImageBytes = imageBytes, Caption = caption };
        }
        catch
        {
            return null;
        }
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

    private async Task<IReadOnlyList<ContentBlock>> WalkElement(IElement element)
    {
        var blocks = new List<ContentBlock>();
        await WalkElementInto(element, blocks);
        return blocks;
    }

    private async Task WalkElementInto(IElement element, List<ContentBlock> blocks)
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
                var imageBlock = await TryExtractHtmlImage(child);
                if (imageBlock != null)
                    blocks.Add(imageBlock);
            }
            else
            {
                await WalkElementInto(child, blocks);
            }
        }
    }

    private async Task<ImageBlock?> TryExtractHtmlImage(IElement figure)
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
