using System.Text.Json;
using Microsoft.Extensions.Options;
using Foreword.Domain.Health;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;
using Storyworthtelling.Voice.Health;

namespace Foreword.Infrastructure.Nyt;

public class NytTopStoriesClient : INytTopStoriesClient
{
    private const string BaseUrl = "https://api.nytimes.com/svc/topstories/v2";

    private readonly HttpClient httpClient;
    private readonly NytApiOptions options;
    private readonly ICredentialHealthSink? healthSink;

    public NytTopStoriesClient(
        HttpClient httpClient, IOptions<NytApiOptions> options, ICredentialHealthSink? healthSink = null)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.healthSink = healthSink;
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
        {
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, null))
                healthSink?.MarkFailing(CredentialIds.NytApiKey, $"{(int)response.StatusCode} {response.StatusCode}");
            throw new HttpRequestException(
                $"NYT Top Stories API failed: {(int)response.StatusCode} ({response.StatusCode})");
        }

        healthSink?.MarkOk(CredentialIds.NytApiKey);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.GetProperty("results")
            .EnumerateArray()
            .Select(el => new TopStory
            {
                Title = el.GetProperty("title").GetString() ?? string.Empty,
                Byline = el.GetProperty("byline").GetString() ?? string.Empty,
                Section = el.GetProperty("section").GetString() ?? string.Empty,
                Url = el.GetProperty("url").GetString() ?? string.Empty,
                Abstract = el.TryGetProperty("abstract", out var abs) ? abs.GetString() ?? string.Empty : string.Empty
            })
            .Where(filter)
            .Take(count)
            .ToList();
    }
}
