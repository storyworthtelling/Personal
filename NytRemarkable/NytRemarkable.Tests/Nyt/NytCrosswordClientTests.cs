using System.Net;
using Microsoft.Extensions.Options;
using NytRemarkable.Infrastructure.Nyt;
using Xunit;

namespace NytRemarkable.Tests.Nyt;

public class NytCrosswordClientTests
{
    private static NytCrosswordClient CreateClient(
        HttpResponseMessage response,
        out List<HttpRequestMessage> capturedRequests)
    {
        var requests = new List<HttpRequestMessage>();
        capturedRequests = requests;

        var handler = new CapturingHandler(response, requests);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new NytOptions { SessionCookie = "test-session-cookie" });
        return new NytCrosswordClient(httpClient, options);
    }

    [Fact]
    public async Task DownloadDailyPuzzle_ReturnsBytes_WhenResponseIsSuccessful()
    {
        var expectedBytes = new byte[] { 1, 2, 3, 4 };
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(expectedBytes) },
            out _);

        var result = await client.DownloadDailyPuzzle(new DateOnly(2025, 1, 15));

        Assert.Equal(expectedBytes, result);
    }

    [Fact]
    public async Task DownloadDailyPuzzle_RequestsCorrectUrl()
    {
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) },
            out var requests);

        await client.DownloadDailyPuzzle(new DateOnly(2025, 3, 7));

        Assert.Single(requests);
        Assert.Equal(
            "https://www.nytimes.com/svc/crosswords/v2/puzzle/print/2025/03/07.pdf",
            requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task DownloadDailyPuzzle_IncludesSessionCookieHeader()
    {
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) },
            out var requests);

        await client.DownloadDailyPuzzle(new DateOnly(2025, 1, 1));

        Assert.True(requests[0].Headers.TryGetValues("Cookie", out var values));
        Assert.Contains("NYT-S=test-session-cookie", values);
    }

    [Fact]
    public async Task DownloadDailyPuzzle_Throws_WhenUnauthorized()
    {
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            out _);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.DownloadDailyPuzzle(new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public async Task DownloadBonusPuzzle_RequestsCorrectUrl()
    {
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) },
            out var requests);

        await client.DownloadBonusPuzzle(new DateOnly(2025, 2, 2));

        Assert.Single(requests);
        Assert.Equal(
            "https://www.nytimes.com/svc/crosswords/v2/puzzle/print/2025/02/02.pdf",
            requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task DownloadBonusPuzzle_ReturnsBytes_WhenResponseIsSuccessful()
    {
        var expectedBytes = new byte[] { 9, 8, 7 };
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(expectedBytes) },
            out _);

        var result = await client.DownloadBonusPuzzle(new DateOnly(2025, 4, 6));

        Assert.Equal(expectedBytes, result);
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
