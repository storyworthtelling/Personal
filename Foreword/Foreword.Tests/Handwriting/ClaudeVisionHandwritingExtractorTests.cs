using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Handwriting;
using Xunit;

namespace Foreword.Tests.Handwriting;

public class ClaudeVisionHandwritingExtractorTests
{
    private static readonly byte[] FakePdf = [37, 80, 68, 70]; // %PDF

    private static (ClaudeVisionHandwritingExtractor extractor, List<CapturedRequest> requests)
        Create(HttpResponseMessage response)
    {
        var requests = new List<CapturedRequest>();
        var httpClient = new HttpClient(new CapturingHandler(response, requests));
        var extractor = new ClaudeVisionHandwritingExtractor(
            httpClient,
            Options.Create(new AnthropicOptions { ApiKey = "test-key", Model = "test-model" }),
            NullLogger<ClaudeVisionHandwritingExtractor>.Instance);
        return (extractor, requests);
    }

    private sealed record CapturedRequest(bool HasApiKey, bool HasVersion, string Body);

    private static HttpResponseMessage AnthropicReply(string innerText) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"content":[{"type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(innerText)}}}]}""")
        };

    [Fact]
    public async Task Extract_ParsesEventNote()
    {
        var (extractor, _) = Create(AnthropicReply(
            """[{"text":"Linden dentist","when":"2026-06-10T09:00:00","column":"Linden","isTask":false,"confidence":0.9}]"""));

        var notes = await extractor.Extract(FakePdf);

        var note = Assert.Single(notes);
        Assert.Equal("Linden dentist", note.Text);
        Assert.Equal(PersonColumn.Linden, note.Column);
        Assert.False(note.IsTask);
        Assert.Equal(0.9, note.Confidence);
        Assert.Equal(new DateTime(2026, 6, 10, 9, 0, 0), note.When);
    }

    [Fact]
    public async Task Extract_ParsesTaskNote()
    {
        var (extractor, _) = Create(AnthropicReply(
            """[{"text":"buy stamps","when":null,"column":null,"isTask":true,"confidence":0.8}]"""));

        var note = Assert.Single(await extractor.Extract(FakePdf));
        Assert.True(note.IsTask);
        Assert.Null(note.When);
        Assert.Null(note.Column);
    }

    [Fact]
    public async Task Extract_KeepsLowConfidenceNotes()
    {
        // The extractor reports confidence; the orchestrator decides what to auto-add.
        var (extractor, _) = Create(AnthropicReply(
            """[{"text":"???","when":null,"column":null,"isTask":false,"confidence":0.2}]"""));

        var note = Assert.Single(await extractor.Extract(FakePdf));
        Assert.Equal(0.2, note.Confidence);
    }

    [Fact]
    public async Task Extract_ToleratesProseAroundJson()
    {
        var (extractor, _) = Create(AnthropicReply(
            """Here are the notes: [{"text":"call mom","isTask":true,"confidence":0.95}] hope that helps"""));

        var note = Assert.Single(await extractor.Extract(FakePdf));
        Assert.Equal("call mom", note.Text);
    }

    [Fact]
    public async Task Extract_EmptyArrayReturnsNoNotes()
    {
        var (extractor, _) = Create(AnthropicReply("[]"));
        Assert.Empty(await extractor.Extract(FakePdf));
    }

    [Fact]
    public async Task Extract_SendsApiKeyAndPdfDocument()
    {
        var (extractor, requests) = Create(AnthropicReply("[]"));

        await extractor.Extract(FakePdf);

        var request = Assert.Single(requests);
        Assert.True(request.HasApiKey);
        Assert.True(request.HasVersion);
        Assert.Contains("\"type\":\"document\"", request.Body);
        Assert.Contains("application/pdf", request.Body);
        Assert.Contains("cache_control", request.Body);
    }

    [Fact]
    public async Task Extract_EmptyPdfSkipsCall()
    {
        var (extractor, requests) = Create(AnthropicReply("[]"));

        var notes = await extractor.Extract([]);

        Assert.Empty(notes);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task Extract_ThrowsOnHttpError()
    {
        var (extractor, _) = Create(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("nope")
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => extractor.Extract(FakePdf));
    }

    private sealed class CapturingHandler(HttpResponseMessage response, List<CapturedRequest> captured)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Read the body here; the request (and its content) is disposed once Extract returns.
            var body = request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
            captured.Add(new CapturedRequest(
                request.Headers.Contains("x-api-key"),
                request.Headers.Contains("anthropic-version"),
                body));
            return response;
        }
    }
}
