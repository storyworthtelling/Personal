using System.Text.Json;
using Microsoft.Extensions.Options;
using NytRemarkable.Domain.Interfaces;
using NytRemarkable.Domain.Models;

namespace NytRemarkable.Infrastructure.Nyt;

public class NytTopStoriesClient : INytTopStoriesClient
{
    private const string BaseUrl = "https://api.nytimes.com/svc/topstories/v2";

    private readonly HttpClient httpClient;
    private readonly NytApiOptions options;

    public NytTopStoriesClient(HttpClient httpClient, IOptions<NytApiOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<IReadOnlyList<TopStory>> GetTopNewsStories(int count) =>
        await FetchStories("home", count, s => s.Section != "Opinion");

    public async Task<IReadOnlyList<TopStory>> GetTopOpinionArticles(int count) =>
        await FetchStories("opinion", count, _ => true);

    private async Task<IReadOnlyList<TopStory>> FetchStories(
        string section, int count, Func<TopStory, bool> filter)
    {
        var url = $"{BaseUrl}/{section}.json?api-key={options.ApiKey}";
        var response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"NYT Top Stories API failed: {(int)response.StatusCode} ({response.StatusCode})");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.GetProperty("results")
            .EnumerateArray()
            .Select(el => new TopStory
            {
                Title = el.GetProperty("title").GetString() ?? string.Empty,
                Byline = el.GetProperty("byline").GetString() ?? string.Empty,
                Section = el.GetProperty("section").GetString() ?? string.Empty,
                Url = el.GetProperty("url").GetString() ?? string.Empty
            })
            .Where(filter)
            .Take(count)
            .ToList();
    }
}
