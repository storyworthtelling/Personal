using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
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
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options));

    /// <summary>A voice that has been learned from one book, for the tests that only need one.</summary>
    private static VoiceProfile Learned(string register, string example) => VoiceProfile.Empty with
    {
        Register = register,
        Traits = ["wry"],
        Examples = [new VoiceExample(example, Guid.NewGuid())],
        Avoid = ["hype"],
        DocumentsAnalyzed = 2,
    };

    [Fact]
    public async Task ComposeAsync_WritesTheMessage_UsingTheFastModelAndTheKey()
    {
        var handler = new FakeHttpMessageHandler().EnqueueOk(
            MessageResponse("At last — the gate is open. Let's begin."));

        var message = await Client(handler).ComposeAsync(
            Learned("Wry and warm.", "At last, the gate."),
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
    public async Task ComposeAsync_UsesTheStrongModel_ForAHighVisibilityMessage()
    {
        var handler = new FakeHttpMessageHandler().EnqueueOk(MessageResponse("At last — the gate is open."));

        await Client(handler).ComposeAsync(
            Learned("Wry and warm.", "At last, the gate."),
            new MessageRequest("welcome", "returning user", HighVisibility: true), CancellationToken.None);

        var body = handler.RequestBodies.Single();
        Assert.Contains("claude-sonnet-4-6", body); // a message the owner reads for its voice runs on the strong tier
        Assert.DoesNotContain("claude-haiku-4-5-20251001", body);
    }

    [Fact]
    public async Task RefineAsync_FoldsTheDocumentIntoTheProfile_OnTheStrongModel()
    {
        var handler = new FakeHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"register":"Wry, warm, economical.","traits":["wry","warm"],"examples":["At last, the gate."],"avoid":["hype"],"cadence":["short declarative openers"],"punctuation":["em-dash asides"]}"""));

        var book = Guid.NewGuid();
        var updated = await Client(handler).RefineAsync(
            VoiceProfile.Empty, "A sample of the owner's writing.", book, CancellationToken.None);

        Assert.Equal("Wry, warm, economical.", updated.Register);
        Assert.Equal(["wry", "warm"], updated.Traits);
        Assert.Equal([new VoiceExample("At last, the gate.", book)], updated.Examples);
        Assert.Equal(["hype"], updated.Avoid);
        Assert.Equal(["short declarative openers"], updated.Cadence);
        Assert.Equal(["em-dash asides"], updated.Punctuation);
        Assert.Equal(1, updated.DocumentsAnalyzed); // provenance counted here, not trusted to the model

        Assert.Contains("claude-sonnet-4-6", handler.RequestBodies.Single()); // strong tier for the analysis
    }

    [Fact]
    public async Task RefineAsync_KeepsEveryOldExample_AndAppendsTheNewOnes()
    {
        // Examples are sticky: the model returns only fresh phrasings from this sample, and the client folds
        // them onto the ones already learned — old anchors are never dropped as more books feed the profile.
        var handler = new FakeHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"register":"s","traits":[],"examples":["a fresh turn of phrase","At last, the gate."],"avoid":[]}"""));

        var earlier = Guid.NewGuid();
        var thisBook = Guid.NewGuid();
        var existing = VoiceProfile.Empty with
        {
            Register = "old",
            Examples = [new VoiceExample("At last, the gate.", earlier), new VoiceExample("the old anchor", earlier)],
            DocumentsAnalyzed = 2,
        };

        var updated = await Client(handler).RefineAsync(existing, "another book", thisBook, CancellationToken.None);

        // Old examples kept in order, the genuinely new one appended, and the echoed duplicate not stored twice.
        Assert.Equal(
            ["At last, the gate.", "the old anchor", "a fresh turn of phrase"],
            updated.Examples.Select(example => example.Text));

        // And the echoed one keeps the book that first taught it, so discarding this book cannot take
        // an anchor the earlier book is still paying for.
        Assert.Equal(earlier, updated.Examples[0].ProjectId);
        Assert.Equal(thisBook, updated.Examples[2].ProjectId);
    }

    [Fact]
    public async Task RefineAsync_DefaultsMissingLists_WhenTheReplyOmitsThem()
    {
        var handler = new FakeHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"register":"Plain-spoken."}"""));

        var updated = await Client(handler).RefineAsync(
            VoiceProfile.Empty, "doc", Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(updated.Cadence);
        Assert.Empty(updated.Punctuation);
        Assert.Empty(updated.Examples);
    }

    [Fact]
    public async Task RefineAsync_IncrementsTheDocumentCount_FromTheExistingProfile()
    {
        var handler = new FakeHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"register":"s","traits":[],"examples":[],"avoid":[]}"""));

        var updated = await Client(handler).RefineAsync(
            VoiceProfile.Empty with { Register = "old", Traits = ["a"], DocumentsAnalyzed = 4 },
            "another document", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(5, updated.DocumentsAnalyzed);
    }

    [Fact]
    public async Task RefineAsync_ToleratesJsonWrappedInProse()
    {
        var handler = new FakeHttpMessageHandler().EnqueueOk(MessageResponse(
            """Here is the profile: {"register":"Plain-spoken.","traits":["plain"],"examples":[],"avoid":[]} — hope that helps!"""));

        var updated = await Client(handler).RefineAsync(
            VoiceProfile.Empty, "doc", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("Plain-spoken.", updated.Register);
        Assert.Equal(1, updated.DocumentsAnalyzed);
    }

    [Fact]
    public async Task RefineAsync_KeepsTheExistingProfile_WhenTheReplyCannotBeParsed()
    {
        // A garbled reply must never corrupt a good profile — the safe outcome is "no change".
        var existing = Learned("Wry and warm.", "At last.") with { DocumentsAnalyzed = 3 };
        var handler = new FakeHttpMessageHandler().EnqueueOk(MessageResponse("sorry, I can't do that"));

        var updated = await Client(handler).RefineAsync(existing, "doc", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(existing, updated);
    }

    [Fact]
    public async Task ComposeAsync_ThrowsDescriptively_OnAnApiError()
    {
        var handler = new FakeHttpMessageHandler().Enqueue(
            HttpStatusCode.Unauthorized, """{"error":{"type":"authentication_error","message":"bad key"}}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).ComposeAsync(
            VoiceProfile.Empty with { Register = "v", Traits = ["a"], DocumentsAnalyzed = 1 },
            new MessageRequest("welcome"), CancellationToken.None));

        Assert.Contains("401", ex.Message);
    }
}
