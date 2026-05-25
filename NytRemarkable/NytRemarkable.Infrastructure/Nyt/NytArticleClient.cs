using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using NytRemarkable.Domain.Interfaces;
using NytRemarkable.Domain.Models;

namespace NytRemarkable.Infrastructure.Nyt;

public class NytArticleClient : INytArticleClient
{
    // Known paths to the article body array in NYT's __NEXT_DATA__ JSON
    private static readonly string[][] BodyPaths =
    [
        ["props", "pageProps", "initialData", "data", "article", "body"],
        ["props", "pageProps", "initialData", "data", "article", "sprinkledBody"],
        ["props", "pageProps", "initialData", "data", "article", "bodyWithSponsorship"],
    ];

    private readonly IArticlePageFetcher pageFetcher;
    private readonly HttpClient httpClient;
    private readonly ILogger<NytArticleClient> logger;

    public NytArticleClient(IArticlePageFetcher pageFetcher, HttpClient httpClient, ILogger<NytArticleClient> logger)
    {
        this.pageFetcher = pageFetcher;
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public async Task<ArticleContent> GetArticle(string url)
    {
        var html = await pageFetcher.FetchPageHtmlAsync(url);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html));

        // Log the <title> tag to confirm we got the article page and not a login/paywall redirect
        var pageTitle = document.QuerySelector("title")?.TextContent.Trim();
        logger.LogInformation("Parsing {Url} — page <title>: {PageTitle}", url, pageTitle);

        var title = document.QuerySelector("h1")?.TextContent.Trim() ?? string.Empty;
        var body = await ExtractArticleBody(document, url);

        var imageCount = body.Count(b => b is ImageBlock);
        var textCount  = body.Count(b => b is TextBlock);
        logger.LogInformation("Parsed {Total} block(s) from {Url} — {Text} text, {Images} image(s)",
            body.Count, url, textCount, imageCount);

        return new ArticleContent
        {
            Title = title,
            Byline = ExtractByline(document),
            Section = ExtractSection(document),
            Body = body
        };
    }

    private async Task<IReadOnlyList<ContentBlock>> ExtractArticleBody(IDocument document, string url)
    {
        var blocks = await TryParseNextData(document, url);
        if (blocks.Count > 0)
            return blocks;

        // HTML fallback
        var articleBody =
            document.QuerySelector("section[name='articleBody']") ??
            document.QuerySelector("[name='articleBody']") ??
            document.QuerySelector("[data-testid='article-body']") ??
            document.QuerySelector("article");

        if (articleBody == null)
        {
            logger.LogWarning("No article body found via any selector for {Url}", url);
            return [];
        }

        var htmlBlocks = await WalkElement(articleBody);
        logger.LogInformation("HTML fallback found {Count} blocks for {Url}", htmlBlocks.Count, url);
        return htmlBlocks;
    }

    private async Task<IReadOnlyList<ContentBlock>> TryParseNextData(IDocument document, string url)
    {
        var script = document.QuerySelector("script#__NEXT_DATA__");
        if (script == null)
        {
            logger.LogWarning("No __NEXT_DATA__ script found for {Url}", url);
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(script.TextContent);

            foreach (var path in BodyPaths)
            {
                var bodyArray = NavigatePath(doc.RootElement, path);
                if (bodyArray is { ValueKind: JsonValueKind.Array })
                {
                    logger.LogInformation("Found body via path [{Path}] for {Url}",
                        string.Join(".", path), url);
                    return await ParseBodyBlocks(bodyArray.Value);
                }
            }

            var pageProps = NavigatePath(doc.RootElement, ["props", "pageProps"]);
            if (pageProps is { ValueKind: JsonValueKind.Object })
            {
                var keys = pageProps.Value.EnumerateObject().Select(p => p.Name);
                logger.LogWarning("No body path matched for {Url}. pageProps keys: {Keys}",
                    url, string.Join(", ", keys));

                var initialData = NavigatePath(pageProps.Value, ["initialData", "data"]);
                if (initialData is { ValueKind: JsonValueKind.Object })
                {
                    var dataKeys = initialData.Value.EnumerateObject().Select(p => p.Name);
                    logger.LogWarning("initialData.data keys for {Url}: {Keys}", url, string.Join(", ", dataKeys));
                }
            }
        }
        catch
        {
            // ignore — fall through to HTML
        }

        return [];
    }

    private static JsonElement? NavigatePath(JsonElement root, string[] path)
    {
        var current = root;
        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object) return null;
            if (!current.TryGetProperty(key, out var next)) return null;
            current = next;
        }
        return current;
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
                if (!string.IsNullOrWhiteSpace(text) && !IsBoilerplate(text))
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
        string? imageUrl = null;

        if (block.TryGetProperty("crops", out var crops))
        {
            var firstCrop = crops.EnumerateArray().FirstOrDefault();
            if (firstCrop.ValueKind == JsonValueKind.Object &&
                firstCrop.TryGetProperty("renditions", out var renditions))
            {
                // Prefer a non-WebP rendition; fall back to last if all are WebP
                var best = renditions.EnumerateArray()
                    .Where(r => r.TryGetProperty("url", out var u) &&
                                !(u.GetString() ?? "").EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                    .Cast<JsonElement?>()
                    .LastOrDefault()
                    ?? renditions.EnumerateArray().Cast<JsonElement?>().LastOrDefault();

                if (best is { ValueKind: JsonValueKind.Object } b &&
                    b.TryGetProperty("url", out var urlEl))
                    imageUrl = urlEl.GetString();
            }
        }

        if (imageUrl == null) return null;

        string? caption = null;
        if (block.TryGetProperty("caption", out var cap))
            caption = ExtractTextFromBlock(cap);

        var fetchUrl = StripWebP(imageUrl);
        try
        {
            var imageBytes = await httpClient.GetByteArrayAsync(fetchUrl);
            logger.LogInformation("Image downloaded: {Bytes} bytes from {Url}", imageBytes.Length, fetchUrl);
            return new ImageBlock { ImageBytes = imageBytes, Caption = caption };
        }
        catch (Exception ex)
        {
            logger.LogWarning("Image download failed for {Url}: {Error}", fetchUrl, ex.Message);
            return null;
        }
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
            if (child.LocalName == "figcaption") continue;

            if (child.LocalName == "p")
            {
                var text = child.TextContent.Trim();
                if (!string.IsNullOrEmpty(text) && !IsBoilerplate(text))
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
            var imageBytes = await httpClient.GetByteArrayAsync(StripWebP(src));
            return new ImageBlock { ImageBytes = imageBytes, Caption = caption };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBoilerplate(string text) =>
        text.Equals("Advertisement", StringComparison.OrdinalIgnoreCase);

    private static string StripWebP(string url) =>
        Regex.Replace(url, @"[?&]auto=webp", m => m.Value.StartsWith('?') ? "?" : "")
             .TrimEnd('?').TrimEnd('&');

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
