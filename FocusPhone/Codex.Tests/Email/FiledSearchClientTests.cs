using System.Net;
using System.Text.Json;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Email;
using Codex.Tests.Support;
using Storyworthtelling.Voice;
using Xunit;

namespace Codex.Tests.Email;

/// <summary>
/// The one call behind a Filed search. The catalogue rides a cached system block so asking for
/// more re-reads it cheaply; the reply comes back with its usage so the log can say what each
/// search cost and how it went.
/// </summary>
public class FiledSearchClientTests
{
    private static readonly VoiceOptions Options = new()
    {
        ApiKey = "sk-test",
        Model = "claude-sonnet-5-5",
        FastModel = "claude-haiku-5-5",
    };

    private static FiledCatalogue Catalogue() => FiledCatalogue.Build(
        [new FiledEntry("a", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, TimelineItemKind.Email, true,
            "robin@example.com", "Quote", "Here it is")],
        query: "quote");

    private static FiledSearchClient Client(QueuedHttpMessageHandler handler, VoiceOptions? options = null) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options));

    private static string Reply(string text) => JsonSerializer.Serialize(new
    {
        content = new[] { new { type = "text", text } },
        stop_reason = "end_turn",
        usage = new { input_tokens = 120, cache_creation_input_tokens = 9000, cache_read_input_tokens = 0, output_tokens = 30 },
    });

    [Fact]
    public async Task AskSendsTheCatalogueCachedAndReturnsTheReplyWithItsUsage()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(Reply("""{"matches":[{"line":1,"fit":"strong"}]}"""));

        var reply = await Client(handler).AskAsync(Catalogue(), "the quote", shownLines: [], new DateOnly(2026, 11, 5));

        Assert.Equal("""{"matches":[{"line":1,"fit":"strong"}]}""", reply.Text);
        Assert.Equal(new FiledUsage(120, 0, 9000, 30), reply.Usage);
        Assert.Equal("end_turn", reply.StopReason);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("sk-test", request.Headers.GetValues("x-api-key").Single());

        using var body = JsonDocument.Parse(handler.RequestBodies.Single());
        Assert.Equal("claude-sonnet-5-5", body.RootElement.GetProperty("model").GetString()); // judgement: strong tier
        var system = body.RootElement.GetProperty("system")[0];
        Assert.Equal("ephemeral", system.GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Contains("robin@example.com", system.GetProperty("text").GetString());
        Assert.Contains("the quote", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public void WithoutAKeyThereIsNothingToAsk()
    {
        Assert.False(Client(new QueuedHttpMessageHandler(), new VoiceOptions()).HasKey);
        Assert.True(Client(new QueuedHttpMessageHandler()).HasKey);
    }

    // A refusal says what Anthropic said, so the log shows a rejected key or a rate limit
    // rather than "search failed".
    [Fact]
    public async Task ARefusalThrowsWithTheStatusAndTheReason()
    {
        var handler = new QueuedHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized,
            """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""");

        var error = await Assert.ThrowsAsync<FiledSearchException>(() =>
            Client(handler).AskAsync(Catalogue(), "q", [], new DateOnly(2026, 11, 5)));

        Assert.Contains("401", error.Message);
        Assert.Contains("invalid x-api-key", error.Message);
    }
}
