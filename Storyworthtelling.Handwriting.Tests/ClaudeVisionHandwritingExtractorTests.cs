using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storyworthtelling.Handwriting;
using Xunit;

namespace Storyworthtelling.Handwriting.Tests;

/// <summary>
/// The generic HTTP-shape/JSON-tolerance/quiet-failure contract, independent of any one caller's
/// instruction wording. Foreword's own re-verification of its exact wording and household column
/// names lives in Foreword.Tests instead — this suite proves the shared mechanics.
/// </summary>
public class ClaudeVisionHandwritingExtractorTests
{
    private const string TestInstruction =
        "Transcribe the hand-written ink in the marked region. Respond with ONLY a JSON array.";
    private const string TestCredentialId = "test-credential";

    private static readonly byte[] FakePdf = [37, 80, 68, 70]; // %PDF

    private static (ClaudeVisionHandwritingExtractor extractor, List<CapturedRequest> requests)
        Create(HttpResponseMessage response)
    {
        var requests = new List<CapturedRequest>();
        var httpClient = new HttpClient(new CapturingHandler(response, requests));
        var extractor = new ClaudeVisionHandwritingExtractor(
            TestInstruction,
            TestCredentialId,
            httpClient,
            Options.Create(new ClaudeVisionOptions { ApiKey = "test-key", Model = "test-model" }),
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
            """[{"text":"Jo dentist","when":"2026-06-10T09:00:00","column":"Jo","isTask":false,"confidence":0.9}]"""));

        var notes = await extractor.Extract(FakePdf);

        var note = Assert.Single(notes);
        Assert.Equal("Jo dentist", note.Text);
        // Column comes back as the model's own raw string — this library does not know Foreword's
        // PersonColumn, or any other caller's set of valid column names.
        Assert.Equal("Jo", note.Column);
        Assert.False(note.IsTask);
        Assert.Equal(0.9, note.Confidence);
        Assert.Equal(new DateTime(2026, 6, 10, 9, 0, 0), note.When);
    }

    /// <summary>
    /// Whatever varies per account (the names of the columns a page prints) rides in the message
    /// the caller supplies, not the cached instruction, so changing a column does not cost the cache.
    /// </summary>
    [Fact]
    public async Task Extract_SendsTheCallersOwnMessageBesideThePage()
    {
        var requests = new List<CapturedRequest>();
        var extractor = new ClaudeVisionHandwritingExtractor(
            TestInstruction,
            TestCredentialId,
            new HttpClient(new CapturingHandler(AnthropicReply("[]"), requests)),
            Options.Create(new ClaudeVisionOptions { ApiKey = "test-key", Model = "test-model" }),
            NullLogger<ClaudeVisionHandwritingExtractor>.Instance,
            userMessage: () => "Transcribe the hand-written notes. The columns are: Alex, Jo.");

        await extractor.Extract(FakePdf);

        var body = Assert.Single(requests).Body;
        Assert.Contains("The columns are: Alex, Jo.", body);
        Assert.DoesNotContain("The columns are", TestInstruction);
    }

    /// <summary>The message is read on every call, so a caller whose data changes is not stale.</summary>
    [Fact]
    public async Task Extract_ReadsTheCallersMessageAfreshEachTime()
    {
        var requests = new List<CapturedRequest>();
        var round = 0;
        var extractor = new ClaudeVisionHandwritingExtractor(
            TestInstruction,
            TestCredentialId,
            new HttpClient(new CapturingHandler(AnthropicReply("[]"), requests)),
            Options.Create(new ClaudeVisionOptions { ApiKey = "test-key", Model = "test-model" }),
            NullLogger<ClaudeVisionHandwritingExtractor>.Instance,
            userMessage: () => $"round {++round}");

        await extractor.Extract(FakePdf);
        await extractor.Extract(FakePdf);

        Assert.Contains("round 1", requests[0].Body);
        Assert.Contains("round 2", requests[1].Body);
    }

    /// <summary>With no message supplied the library says only what every caller needs said.</summary>
    [Fact]
    public async Task Extract_WithoutAMessageSaysOnlyToTranscribe()
    {
        var (extractor, requests) = Create(AnthropicReply("[]"));

        await extractor.Extract(FakePdf);

        Assert.Contains("\"text\":\"Transcribe the hand-written notes.\"", Assert.Single(requests).Body);
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
        // The extractor reports confidence; the caller decides what to auto-add.
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

    /// <summary>
    /// The instruction is entirely the caller's own — this is what makes the extractor reusable
    /// for a caller whose region, columns and household names look nothing like Foreword's.
    /// </summary>
    [Fact]
    public async Task Extract_SendsTheCallersOwnInstructionTextVerbatim()
    {
        var (extractor, requests) = Create(AnthropicReply("[]"));

        await extractor.Extract(FakePdf);

        var request = Assert.Single(requests);
        Assert.Contains(TestInstruction, request.Body);
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
