using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Foreword.Domain.Health;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Foreword.Infrastructure.Handwriting;
using Storyworthtelling.Handwriting;
using Xunit;

namespace Foreword.Tests.Handwriting;

/// <summary>
/// Foreword's own re-verification that the extraction to Storyworthtelling.Handwriting (Index-P11
/// Release F) left its runtime behavior unchanged. The shared library's own test suite
/// (Storyworthtelling.Handwriting.Tests) proves the generic HTTP-shape/JSON-tolerance/quiet-failure
/// contract; this suite proves Foreword still feeds it its own wording, the account's own column
/// names (as data, in the message beside the page) and the credential id it always did.
/// </summary>
public class HandwritingInstructionTests
{
    private static readonly byte[] FakePdf = [37, 80, 68, 70]; // %PDF

    /// <summary>
    /// Pinned so a future edit to the wording — the region description, the JSON schema — is a
    /// decision made here, not a silent drift in the constructor argument. The column names are
    /// not in it: they are each account's own and arrive with the page.
    /// </summary>
    [Fact]
    public void TextIsTheFrozenWordingMovedFromTheOldExtractor()
    {
        Assert.Equal(
            "You are reading a daily planner page that was printed and then hand-written on with a " +
            "stylus. Ignore all printed/typed text and the printed grid. Look ONLY at the hand-written " +
            "ink in the 'write below' region near the bottom of the page. Transcribe each hand-written " +
            "future event or task. Respond with ONLY a JSON array (no prose). Each element: " +
            "{\"text\": string, \"when\": ISO-8601 date or date-time or null, " +
            "\"column\": one of the column names given with the page, exactly as written, or null, " +
            "\"isTask\": boolean (true for a to-do, false for a calendar event), " +
            "\"confidence\": number 0..1 for how confident you are in the transcription}. " +
            "If the region is empty, return []. Use low confidence when the writing is hard to read.",
            HandwritingInstruction.Text);
    }

    [Fact]
    public void MessageNamesThisAccountsPrintedColumnsInOrder()
    {
        Assert.Equal(
            "Transcribe the hand-written notes. The columns are: Alex, Jo, Watch.",
            HandwritingInstruction.Message([new PersonColumn("Alex"), new PersonColumn("Jo"), PersonColumn.Watch]));
    }

    // The columns as an account set them up in Foreword: invented names, since the code never knows any.
    private static readonly CalendarMappingOptions Mapping = new()
    {
        Columns = [new ColumnChoice { Name = "Alex", HasAccount = true }, new ColumnChoice { Name = "Jo" }],
    };

    private sealed record CapturedRequest(bool HasApiKey, bool HasVersion, string Body);

    private static HttpResponseMessage AnthropicReply(string innerText) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"content":[{"type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(innerText)}}}]}""")
        };

    private sealed class CapturingHandler(HttpResponseMessage response, List<CapturedRequest> captured)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
            captured.Add(new CapturedRequest(
                request.Headers.Contains("x-api-key"),
                request.Headers.Contains("anthropic-version"),
                body));
            return response;
        }
    }

    /// <summary>
    /// Foreword's DI registration builds the shared extractor with
    /// <see cref="HandwritingInstruction.Text"/>, <see cref="CredentialIds.Anthropic"/> and a message
    /// built from this account's columns — this exercises that exact combination (rather than
    /// MauiProgram.cs, which cannot be compiled or run outside CI) and asserts the wording and the
    /// account's column names actually reach the wire, the names in the message and not the
    /// cached instruction.
    /// </summary>
    [Fact]
    public async Task Extract_SendsForewordsInstructionWithThisAccountsColumnsAndCredentialHealth()
    {
        var requests = new List<CapturedRequest>();
        var httpClient = new HttpClient(new CapturingHandler(AnthropicReply("[]"), requests));
        var healthSink = new RecordingHealthSink();

        var extractor = new ClaudeVisionHandwritingExtractor(
            HandwritingInstruction.Text,
            CredentialIds.Anthropic,
            httpClient,
            Options.Create(new ClaudeVisionOptions { ApiKey = "test-key" }),
            NullLogger<ClaudeVisionHandwritingExtractor>.Instance,
            healthSink,
            () => HandwritingInstruction.Message(Mapping.Layout().Printed));

        await extractor.Extract(FakePdf);

        var request = Assert.Single(requests);
        Assert.Contains("The columns are: Alex, Jo, Watch.", request.Body);
        Assert.DoesNotContain("The columns are", HandwritingInstruction.Text);
        Assert.Contains("write below", request.Body);
        Assert.Equal(CredentialIds.Anthropic, healthSink.LastOk);
    }

    /// <summary>
    /// The string the model returns for "column" is generic on the shared library's side —
    /// Foreword is the one that knows PersonColumn, and parses it back on the delivery side
    /// (DailyDeliveryService.ParseColumn, which accepts only a column this account prints), not
    /// inside the extractor any more.
    /// </summary>
    [Fact]
    public async Task Extract_ReturnsTheColumnAsTheRawStringForForewordToParse()
    {
        var httpClient = new HttpClient(new CapturingHandler(
            AnthropicReply("""[{"text":"Jo dentist","column":"Jo","isTask":false,"confidence":0.9}]"""),
            []));

        var extractor = new ClaudeVisionHandwritingExtractor(
            HandwritingInstruction.Text,
            CredentialIds.Anthropic,
            httpClient,
            Options.Create(new ClaudeVisionOptions { ApiKey = "test-key" }),
            NullLogger<ClaudeVisionHandwritingExtractor>.Instance);

        var note = Assert.Single(await extractor.Extract(FakePdf));

        Assert.Equal("Jo", note.Column);
    }

    private sealed class RecordingHealthSink : Storyworthtelling.Voice.Health.ICredentialHealthSink
    {
        public string? LastOk { get; private set; }
        public string? LastFailing { get; private set; }

        public void MarkOk(string credentialId) => LastOk = credentialId;
        public void MarkFailing(string credentialId, string message) => LastFailing = credentialId;
    }
}
