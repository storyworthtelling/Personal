using System.Text.Json;
using Microsoft.Extensions.Logging;
using Foreword.Domain.Brief;
using Foreword.Domain.Interfaces;

namespace Foreword.Infrastructure.Brief;

/// <summary>
/// xkcd, which is keyless (F14). The field names are the ones a live response actually carries:
/// <c>num</c>, <c>title</c>, <c>alt</c> (the hover text) and <c>img</c>.
///
/// <para>The hover text is fetched with the strip and printed under it, because on paper there is
/// nothing to hover — and the hover text is the second joke.</para>
///
/// <para>Every failure is quiet and returns nothing. A comic is the most skippable thing in the
/// document; a morning without one is a shorter brief, and a morning that did not arrive because
/// xkcd was down is a bug.</para>
/// </summary>
public class XkcdClient(HttpClient httpClient, ILogger<XkcdClient> logger) : IComicClient
{
    private const string LatestUrl = "https://xkcd.com/info.0.json";

    public async Task<int?> Latest()
    {
        if (await Read(LatestUrl) is not { } strip)
        {
            return null;
        }

        return strip.TryGetProperty("num", out var number) && number.TryGetInt32(out var value)
            ? value
            : null;
    }

    public async Task<ComicStrip?> Get(int number)
    {
        // The API has no endpoint for the missing comic and never will — the gap is the joke.
        if (number == ComicSelection.MissingComic)
        {
            return null;
        }

        if (await Read($"https://xkcd.com/{number}/info.0.json") is not { } strip)
        {
            return null;
        }

        var image = Text(strip, "img");
        if (string.IsNullOrEmpty(image) || await Download(image) is not { } bytes)
        {
            // A strip with no picture is not a strip. Printing its title and hover text alone would
            // read as a rendering fault rather than as a comic.
            return null;
        }

        return new ComicStrip(
            number,
            Text(strip, "title"),
            Text(strip, "alt"),
            bytes);
    }

    private async Task<JsonElement?> Read(string url)
    {
        try
        {
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("xkcd {Url} answered {Status}", url, response.StatusCode);
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogInformation(ex, "xkcd {Url} could not be read", url);
            return null;
        }
    }

    private async Task<byte[]?> Download(string url)
    {
        try
        {
            var response = await httpClient.GetAsync(url);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogInformation(ex, "xkcd image {Url} could not be fetched", url);
            return null;
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
