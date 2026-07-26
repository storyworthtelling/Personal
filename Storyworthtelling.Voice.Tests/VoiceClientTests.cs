using System.Net;
using System.Text.Json;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Tests.Support;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

public class VoiceClientTests
{
    private static readonly VoiceOptions Options = new()
    {
        ApiKey = "sk-test",
        Model = "claude-sonnet-4-6",
        FastModel = "claude-haiku-4-5-20251001"
    };

    // Builds a non-streaming Anthropic Messages response carrying one text block.
    private static string MessageResponse(string text) =>
        JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "text", text } },
            stop_reason = "end_turn"
        });

    private static VoiceClient Client(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), Options);

    [Fact]
    public async Task ComposeAsync_WritesTheMessage_UsingTheFastModelAndTheKey()
    {
        var handler = new FakeHttpMessageHandler().EnqueueOk(
            MessageResponse("At last — the gate is open. Let's begin."));

        var message = await Client(handler).ComposeAsync(
            new VoiceProfile("Wry and warm.", ["wry"], ["At last, the gate."], ["hype"], DocumentsAnalyzed: 2),
            new MessageRequest("welcome", "returning user"), CancellationToken.None);

        Assert.Equal("At last — the gate is open. Let's begin.", message);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("sk-test", request.Headers.GetValues("x-api-key").Single());

        var body = handler.RequestBodies.Single();
        Assert.Contains("claude-haiku-4-5-20251001", body); // cheap tier for the high-frequency compose
        Assert.Contains("welcome", body);
    }

    [Fact]
    public async Task RefineAsync_FoldsTheDocumentIntoTheProfile_OnTheStrongModel()
    {
        var handler = new FakeHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"summary":"Wry, warm, economical.","traits":["wry","warm"],"examples":["At last, the gate."],"avoid":["hype"]}"""));

        var updated = await Client(handler).RefineAsync(
            VoiceProfile.Empty, "A sample of the owner's writing.", CancellationToken.None);

        Assert.Equal("Wry, warm, economical.", updated.Summary);
        Assert.Equal(["wry", "warm"], updated.Traits);
        Assert.Equal(["At last, the gate."], updated.Examples);
        Assert.Equal(["hype"], updated.Avoid);
        Assert.Equal(1, updated.DocumentsAnalyzed); // provenance counted here, not trusted to the model

        Assert.Contains("claude-sonnet-4-6", handler.RequestBodies.Single()); // strong tier for the analysis
    }

    [Fact]
    public async Task RefineAsync_IncrementsTheDocumentCount_FromTheExistingProfile()
    {
        var handler = new FakeHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"summary":"s","traits":[],"examples":[],"avoid":[]}"""));

        var updated = await Client(handler).RefineAsync(
            new VoiceProfile("old", ["a"], [], [], DocumentsAnalyzed: 4),
            "another document", CancellationToken.None);

        Assert.Equal(5, updated.DocumentsAnalyzed);
    }

    [Fact]
    public async Task RefineAsync_ToleratesJsonWrappedInProse()
    {
        var handler = new FakeHttpMessageHandler().EnqueueOk(MessageResponse(
            """Here is the profile: {"summary":"Plain-spoken.","traits":["plain"],"examples":[],"avoid":[]} — hope that helps!"""));

        var updated = await Client(handler).RefineAsync(
            VoiceProfile.Empty, "doc", CancellationToken.None);

        Assert.Equal("Plain-spoken.", updated.Summary);
        Assert.Equal(1, updated.DocumentsAnalyzed);
    }

    [Fact]
    public async Task RefineAsync_KeepsTheExistingProfile_WhenTheReplyCannotBeParsed()
    {
        // A garbled reply must never corrupt a good profile — the safe outcome is "no change".
        var existing = new VoiceProfile("Wry and warm.", ["wry"], ["At last."], ["hype"], DocumentsAnalyzed: 3);
        var handler = new FakeHttpMessageHandler().EnqueueOk(MessageResponse("sorry, I can't do that"));

        var updated = await Client(handler).RefineAsync(existing, "doc", CancellationToken.None);

        Assert.Equal(existing, updated);
    }

    [Fact]
    public async Task ComposeAsync_ThrowsDescriptively_OnAnApiError()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(
            HttpStatusCode.Unauthorized, """{"error":{"type":"authentication_error","message":"bad key"}}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).ComposeAsync(
            new VoiceProfile("v", ["a"], [], [], 1), new MessageRequest("welcome"), CancellationToken.None));

        Assert.Contains("401", ex.Message);
    }
}
