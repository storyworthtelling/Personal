using System.Net;
using Codex.Infrastructure.Email;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Health;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Audit 11 §10: a refused key, a rate limit or an empty balance each answered "no idea", so the sorter asked again for
/// every one of up to 300 messages, every sync. A failed call is now told apart from a reply that placed nothing, so the
/// sync stops asking at the first one, and the refusal reaches the <c>claude</c> health entry.
/// </summary>
public class MailReadingFailureTests
{
    private const string Claude = "https://api.anthropic.com/v1/messages";

    private sealed class Sink : ICredentialHealthSink
    {
        public List<string> Lines { get; } = [];
        public void MarkOk(string credentialId) => Lines.Add($"ok {credentialId}");
        public void MarkFailing(string credentialId, string message) => Lines.Add($"failing {credentialId}");
    }

    private static MailReadingClient Reader(HttpStatusCode status, string body, ICredentialHealthSink? health = null, string key = "test-key")
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(Claude, body, status: status);
        return new MailReadingClient(new HttpClient(handler), Options.Create(new VoiceOptions { ApiKey = key }), health);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}")]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\",\"message\":\"slow down\"}}")]
    [InlineData((HttpStatusCode)529, "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"busy\"}}")]
    public async Task AFailedCallIsSaidToHaveFailed(HttpStatusCode status, string body)
    {
        var reading = await Reader(status, body).ReadAsync("a@example.com", "Hello", "Body");

        Assert.True(reading.CallFailed);
        Assert.Null(reading.Kind);
    }

    [Fact]
    public async Task ARefusedKeyReachesTheClaudeHealthEntry()
    {
        var sink = new Sink();

        await Reader(HttpStatusCode.Unauthorized,
            "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}", sink)
            .ReadAsync("a@example.com", "Hello", "Body");

        Assert.Contains($"failing {SharedCredentialIds.Claude}", sink.Lines);
    }

    [Fact]
    public async Task NoKeyIsRulesOnlyNotAFailure()
    {
        var reading = await Reader(HttpStatusCode.OK, "{}", key: "").ReadAsync("a@example.com", "Hello", "Body");

        Assert.False(reading.CallFailed);
    }

    [Fact]
    public async Task AnAnswerThatPlacesNothingIsNotAFailedCall()
    {
        var reply = "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"not json\"}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";

        var reading = await Reader(HttpStatusCode.OK, reply).ReadAsync("a@example.com", "Hello", "Body");

        Assert.False(reading.CallFailed);
        Assert.Null(reading.Kind);
    }
}
