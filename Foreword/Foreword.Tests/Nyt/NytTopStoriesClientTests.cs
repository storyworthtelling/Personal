using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Foreword.Infrastructure.Nyt;
using Xunit;

namespace Foreword.Tests.Nyt;

public class NytTopStoriesClientTests
{
    private static NytTopStoriesClient CreateClient(
        HttpResponseMessage response,
        out List<HttpRequestMessage> capturedRequests)
    {
        var requests = new List<HttpRequestMessage>();
        capturedRequests = requests;
        var handler = new CapturingHandler(response, requests);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new NytApiOptions { ApiKey = "test-api-key" });
        return new NytTopStoriesClient(httpClient, options);
    }

    private static HttpResponseMessage StoriesResponse(
        params (string title, string byline, string section, string url, string @abstract)[] stories)
    {
        var results = stories.Select(s => new
        {
            title = s.title,
            byline = s.byline,
            section = s.section,
            url = s.url,
            @abstract = s.@abstract
        });
        var json = JsonSerializer.Serialize(new { status = "OK", results });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task GetTopNewsStories_CallsHomeSection()
    {
        var client = CreateClient(StoriesResponse(), out var requests);

        await client.GetTopNewsStories(5);

        Assert.Single(requests);
        Assert.Contains("/svc/topstories/v2/home.json", requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetTopNewsStories_IncludesApiKeyQueryParam()
    {
        var client = CreateClient(StoriesResponse(), out var requests);

        await client.GetTopNewsStories(5);

        Assert.Contains("api-key=test-api-key", requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task GetTopNewsStories_ReturnsUpToCountNonOpinionStories()
    {
        var response = StoriesResponse(
            ("News 1", "By A", "U.S.", "https://nytimes.com/1", ""),
            ("Opinion 1", "By B", "Opinion", "https://nytimes.com/2", ""),
            ("News 2", "By C", "World", "https://nytimes.com/3", ""),
            ("News 3", "By D", "Business", "https://nytimes.com/4", ""),
            ("News 4", "By E", "Science", "https://nytimes.com/5", ""),
            ("News 5", "By F", "Health", "https://nytimes.com/6", ""),
            ("News 6", "By G", "U.S.", "https://nytimes.com/7", ""));
        var client = CreateClient(response, out _);

        var stories = await client.GetTopNewsStories(5);

        Assert.Equal(5, stories.Count);
        Assert.DoesNotContain(stories, s => s.Section == "Opinion");
    }

    [Fact]
    public async Task GetTopNewsStories_MapsTitleBylineUrlAndAbstract()
    {
        var response = StoriesResponse(
            ("Big Story", "By Jane Smith", "U.S.", "https://nytimes.com/big-story", "A brief summary."));
        var client = CreateClient(response, out _);

        var stories = await client.GetTopNewsStories(5);

        Assert.Single(stories);
        Assert.Equal("Big Story", stories[0].Title);
        Assert.Equal("By Jane Smith", stories[0].Byline);
        Assert.Equal("U.S.", stories[0].Section);
        Assert.Equal("https://nytimes.com/big-story", stories[0].Url);
        Assert.Equal("A brief summary.", stories[0].Abstract);
    }

    [Fact]
    public async Task GetTopOpinionArticles_CallsOpinionSection()
    {
        var client = CreateClient(StoriesResponse(), out var requests);

        await client.GetTopOpinionArticles(3);

        Assert.Single(requests);
        Assert.Contains("/svc/topstories/v2/opinion.json", requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetTopOpinionArticles_ReturnsUpToCountStories()
    {
        var response = StoriesResponse(
            ("Op 1", "By A", "Opinion", "https://nytimes.com/op1", ""),
            ("Op 2", "By B", "Opinion", "https://nytimes.com/op2", ""),
            ("Op 3", "By C", "Opinion", "https://nytimes.com/op3", ""),
            ("Op 4", "By D", "Opinion", "https://nytimes.com/op4", ""));
        var client = CreateClient(response, out _);

        var stories = await client.GetTopOpinionArticles(3);

        Assert.Equal(3, stories.Count);
    }

    private sealed class CapturingHandler(HttpResponseMessage response, List<HttpRequestMessage> captured)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            captured.Add(request);
            return Task.FromResult(response);
        }
    }
}
