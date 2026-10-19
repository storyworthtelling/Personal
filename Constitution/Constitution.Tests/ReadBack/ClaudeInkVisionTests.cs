using System.Net;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Credentials;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.ReadBack;
using Microsoft.Extensions.Options;
using Moq;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Voice.Health;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// The real vision read, against a stand-in for the service. What matters: it sends the page and the box list and
/// nothing else, it keeps the standing instruction cacheable, it tells the credential-health report only what the
/// service said about the key, and a reply it cannot make sense of is pages not read.
/// </summary>
public class ClaudeInkVisionTests
{
    private sealed class Handler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, Body);
        }
    }

    private static readonly byte[] Pdf = [37, 80, 68, 70, 45, 49];

    private static ClaudeVisionOptions Options(string key = "sample-test-value") => new() { ApiKey = key, Model = "test-model" };

    private static HttpResponseMessage Reply(string text, string stopReason = "end_turn") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } }, stop_reason = stopReason }), Encoding.UTF8, "application/json"),
    };

    private static string Everything(ExpectedDocument document) =>
        "{\"penStrokes\":\"seen\",\"targets\":[" + string.Join(",", document.Pages.SelectMany(page => page.AskedTargets.Select(target =>
            $"{{\"page\":{page.Number},\"id\":\"{target.Id}\",\"marked\":{(page.Number == 2 && target.Id == "done" ? "true" : "false")},\"text\":null,\"alternative\":null,\"confidence\":0.95}}"))) + "]}";

    private static (ClaudeInkVision Vision, Handler Handler, Mock<ICredentialHealthSink> Health) Make(
        Func<HttpRequestMessage, string, HttpResponseMessage> respond, string key = "sample-test-value")
    {
        var handler = new Handler(respond);
        var health = new Mock<ICredentialHealthSink>();
        return (new ClaudeInkVision(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options(key)), health.Object), handler, health);
    }

    [Fact]
    public async Task ItKeepsTheKeyInTheHeaderOnly()
    {
        var document = Workout();
        var (vision, handler, _) = Make((_, _) => Reply(Everything(document)));

        await vision.Read(document, Pdf, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Request.RequestUri!.ToString());
        Assert.Equal("sample-test-value", Assert.Single(handler.Request.Headers.GetValues("x-api-key")));
        Assert.Equal("2023-06-01", Assert.Single(handler.Request.Headers.GetValues("anthropic-version")));
        Assert.DoesNotContain("sample-test-value", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItSendsThePageAndTheBoxListAndNothingElse()
    {
        var document = Workout();
        var (vision, handler, _) = Make((_, _) => Reply(Everything(document)));

        await vision.Read(document, Pdf, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body);
        var root = body.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        var content = root.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        var page = content[0];
        Assert.Equal("document", page.GetProperty("type").GetString());
        Assert.Equal("application/pdf", page.GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal(Convert.ToBase64String(Pdf), page.GetProperty("source").GetProperty("data").GetString());
        Assert.Equal(InkInstruction.Request(document), content[1].GetProperty("text").GetString());
        Assert.DoesNotContain(Person, handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(document.Name, handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStandingInstructionIsTheCacheablePrefix()
    {
        var document = Workout();
        var (vision, handler, _) = Make((_, _) => Reply(Everything(document)));

        await vision.Read(document, Pdf, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body);
        var system = body.RootElement.GetProperty("system")[0];
        Assert.Equal(InkInstruction.System, system.GetProperty("text").GetString());
        Assert.Equal("ephemeral", system.GetProperty("cache_control").GetProperty("type").GetString());
        Assert.True(body.RootElement.GetProperty("max_tokens").GetInt32() >= 2048);
    }

    [Fact]
    public async Task ARepliedReadIsTurnedIntoPagesAndBoxes()
    {
        var document = Workout();
        var (vision, _, _) = Make((_, _) => Reply(Everything(document)));

        var read = await vision.Read(document, Pdf, CancellationToken.None);

        Assert.Equal(PenStrokes.Seen, read.PenStrokes);
        Assert.True(read.PageFor(2).For("done")!.Marked);
        Assert.False(read.PageFor(3).For("done")!.Marked);
    }

    [Fact]
    public async Task AReplyThatIsNotAnAnswerIsPagesNotRead()
    {
        var document = Workout();
        var (vision, _, _) = Make((_, _) => Reply("I am sorry, I cannot see these pages."));

        var read = await vision.Read(document, Pdf, CancellationToken.None);

        Assert.False(read.PageFor(2).WasRead);
        Assert.False(read.PageFor(3).WasRead);
    }

    [Fact]
    public async Task AReplyCutOffForLengthIsPagesNotRead()
    {
        var document = Workout();
        var (vision, _, _) = Make((_, _) => Reply(Everything(document)[..60], "max_tokens"));

        var read = await vision.Read(document, Pdf, CancellationToken.None);

        Assert.False(read.PageFor(2).WasRead);
    }

    [Fact]
    public async Task ABodyWithNoTextIsPagesNotRead()
    {
        var document = Workout();
        var (vision, _, _) = Make((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"content\":[]}") });

        Assert.False((await vision.Read(document, Pdf, CancellationToken.None)).PageFor(2).WasRead);
    }

    [Fact]
    public async Task ARefusedKeyFlagsTheCredential()
    {
        var (vision, _, health) = Make((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":{\"message\":\"invalid x-api-key\"}}") });

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => vision.Read(Workout(), Pdf, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, thrown.StatusCode);
        health.Verify(sink => sink.MarkFailing(CredentialIds.Claude, It.IsAny<string>()), Times.Once);
        health.Verify(sink => sink.MarkOk(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AServerErrorIsAFailureThatSaysNothingAboutTheKey()
    {
        var (vision, _, health) = Make((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("overloaded") });

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => vision.Read(Workout(), Pdf, CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, thrown.StatusCode);
        health.Verify(sink => sink.MarkFailing(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        health.Verify(sink => sink.MarkOk(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ASuccessfulCallMarksTheKeyWorking()
    {
        var document = Workout();
        var (vision, _, health) = Make((_, _) => Reply(Everything(document)));

        await vision.Read(document, Pdf, CancellationToken.None);

        health.Verify(sink => sink.MarkOk(CredentialIds.Claude), Times.Once);
    }

    [Fact]
    public async Task AFailureNamesTheStatusAndTheReason()
    {
        var (vision, _, _) = Make((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"Your credit balance is too low.\"}}"),
        });

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => vision.Read(Workout(), Pdf, CancellationToken.None));

        Assert.Contains("400", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("credit balance", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Pdf), thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAKeyItSaysSoAndSendsNothing()
    {
        var (vision, handler, _) = Make((_, _) => Reply("{}"), key: "  ");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => vision.Read(Workout(), Pdf, CancellationToken.None));

        Assert.Contains("key", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ADocumentWithNoInkIsNotSent()
    {
        var (vision, handler, _) = Make((_, _) => Reply("{}"));

        await Assert.ThrowsAsync<ArgumentException>(() => vision.Read(Digest(), Pdf, CancellationToken.None));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AnEmptyPdfIsNotSent()
    {
        var (vision, handler, _) = Make((_, _) => Reply("{}"));

        await Assert.ThrowsAsync<ArgumentException>(() => vision.Read(Workout(), [], CancellationToken.None));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ACancelledCallStaysACancellation()
    {
        using var cancel = new CancellationTokenSource();
        var (vision, _, _) = Make((_, _) => throw new OperationCanceledException(cancel.Token));
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vision.Read(Workout(), Pdf, cancel.Token));
    }
}
