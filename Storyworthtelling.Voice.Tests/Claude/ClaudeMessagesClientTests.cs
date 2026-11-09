using System.Net;
using System.Text.Json;
using Storyworthtelling.Voice.Claude;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Tests.Support;

namespace Storyworthtelling.Voice.Tests.Claude;

/// <summary>
/// The one Messages API client (audit 01 §4). Eighteen apps' worth of clients each re-declared the URL, the
/// version header and the text-block walk, and differed where it mattered: ten reported nothing to credential
/// health, and only Folio told a rate limit from any other failure.
/// </summary>
public class ClaudeMessagesClientTests
{
    private const string Reply = """
        {"id":"msg_1","content":[{"type":"text","text":"Hello "},{"type":"tool_use","id":"t"},{"type":"text","text":"there"}],
         "stop_reason":"end_turn","usage":{"input_tokens":12,"output_tokens":3,"cache_creation_input_tokens":40,"cache_read_input_tokens":7}}
        """;

    private sealed class Health : ICredentialHealthSink
    {
        public List<string> Marks { get; } = [];

        public void MarkOk(string credentialId) => Marks.Add("ok " + credentialId);

        public void MarkFailing(string credentialId, string message) => Marks.Add("failing " + credentialId);
    }

    private static (ClaudeMessagesClient Client, FakeHttpMessageHandler Http, Health Health) Make(string? key = "test-key")
    {
        var http = new FakeHttpMessageHandler();
        var health = new Health();
        return (new ClaudeMessagesClient(new HttpClient(http), () => key, health), http, health);
    }

    [Fact]
    public async Task APlainRequestIsSentAsTheOldClientsSentIt()
    {
        var (client, http, _) = Make();
        http.EnqueueOk(Reply);

        await client.SendAsync(ClaudeRequest.Plain("claude-test", 200, "Be brief.", "Say hello."), CancellationToken.None);

        var sent = http.Requests.Single();
        Assert.Equal("https://api.anthropic.com/v1/messages", sent.RequestUri!.ToString());
        Assert.Equal("test-key", sent.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", sent.Headers.GetValues("anthropic-version").Single());

        using var body = JsonDocument.Parse(http.RequestBodies.Single());
        Assert.Equal("claude-test", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(200, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("Be brief.", body.RootElement.GetProperty("system").GetString());
        var message = body.RootElement.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("Say hello.", message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task BlocksCarryImagesDocumentsAndCacheMarks()
    {
        var (client, http, _) = Make();
        http.EnqueueOk(Reply);

        var request = new ClaudeRequest("claude-test", 100,
            System: [ClaudeBlock.Text("Rules.", cache: true)],
            User: [ClaudeBlock.Pdf("UERG", cache: true), ClaudeBlock.Image("image/jpeg", "SlBH"), ClaudeBlock.Text("What is on the page?")]);
        await client.SendAsync(request, CancellationToken.None);

        using var body = JsonDocument.Parse(http.RequestBodies.Single());
        var system = body.RootElement.GetProperty("system")[0];
        Assert.Equal("text", system.GetProperty("type").GetString());
        Assert.Equal("ephemeral", system.GetProperty("cache_control").GetProperty("type").GetString());

        var content = body.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("document", content[0].GetProperty("type").GetString());
        Assert.Equal("application/pdf", content[0].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal("UERG", content[0].GetProperty("source").GetProperty("data").GetString());
        Assert.True(content[0].TryGetProperty("cache_control", out _));
        Assert.Equal("image", content[1].GetProperty("type").GetString());
        Assert.Equal("base64", content[1].GetProperty("source").GetProperty("type").GetString());
        Assert.False(content[1].TryGetProperty("cache_control", out _));
        Assert.Equal("What is on the page?", content[2].GetProperty("text").GetString());
    }

    [Fact]
    public async Task TheReplyIsTheTextBlocksJoinedWithTheUsage()
    {
        var (client, http, health) = Make();
        http.EnqueueOk(Reply);

        var reply = await client.SendAsync(ClaudeRequest.Plain("m", 10, "s", "u"), CancellationToken.None);

        Assert.Equal("Hello there", reply.Text);
        Assert.Equal("end_turn", reply.StopReason);
        Assert.Equal(new ClaudeUsage(12, 3, 40, 7), reply.Usage);
        Assert.Equal(["ok claude"], health.Marks);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}", ClaudeFailure.KeyRejected)]
    [InlineData(HttpStatusCode.Forbidden, "{}", ClaudeFailure.KeyRejected)]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"error\":{\"type\":\"rate_limit_error\"}}", ClaudeFailure.RateLimited)]
    [InlineData((HttpStatusCode)529, "{\"error\":{\"type\":\"overloaded_error\"}}", ClaudeFailure.Overloaded)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Your credit balance is too low to access the Anthropic API.\"}}", ClaudeFailure.QuotaExhausted)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"messages: field required\"}}", ClaudeFailure.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, "oops", ClaudeFailure.Failed)]
    public async Task AFailureSaysWhatKindItIs(HttpStatusCode status, string body, ClaudeFailure kind)
    {
        var (client, http, health) = Make();
        http.Enqueue(status, body);

        var error = await Assert.ThrowsAsync<ClaudeRequestException>(() =>
            client.SendAsync(ClaudeRequest.Plain("m", 10, "s", "u"), CancellationToken.None));

        Assert.Equal(kind, error.Failure);
        Assert.Equal((int)status, (int)error.StatusCode!);
        Assert.IsAssignableFrom<HttpRequestException>(error); // every client's existing catch still holds
        Assert.Equal(kind == ClaudeFailure.KeyRejected ? ["failing claude"] : [], health.Marks);
    }

    [Fact]
    public async Task ARateLimitCarriesTheServersRetryAfter()
    {
        var (client, http, _) = Make();
        http.Enqueue(HttpStatusCode.TooManyRequests, "{}");
        http.LastResponseHeaders("retry-after", "30");

        var error = await Assert.ThrowsAsync<ClaudeRequestException>(() =>
            client.SendAsync(ClaudeRequest.Plain("m", 10, "s", "u"), CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Fact]
    public async Task WithNoKeyNothingIsSent()
    {
        var (client, http, health) = Make(key: " ");

        Assert.False(client.HasKey);
        var error = await Assert.ThrowsAsync<ClaudeRequestException>(() =>
            client.SendAsync(ClaudeRequest.Plain("m", 10, "s", "u"), CancellationToken.None));

        Assert.Equal(ClaudeFailure.NoKey, error.Failure);
        Assert.Empty(http.Requests);
        Assert.Empty(health.Marks);
    }

    [Fact]
    public void TheModelsAreNamedOnce()
    {
        string[] tiers = [ClaudeModels.Standard, ClaudeModels.Fast, ClaudeModels.Strong];
        Assert.All(tiers, model => Assert.StartsWith("claude-", model, StringComparison.Ordinal));
        Assert.Equal(3, tiers.Distinct().Count());
    }
}
