using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Constitution.Domain.Credentials;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts;
using Microsoft.Extensions.Options;
using Moq;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Voice.Health;
using Judge = Constitution.Infrastructure.Meals.ClaudeTasteJudge;
using static Constitution.Tests.MealTaste.TasteData;

namespace Constitution.Tests.ClaudeTasteJudge;

/// <summary>The real judge over the Messages API against a stand-in: only a dish name and a written reason leave, the reply is read strictly, and every failure is a typed "no judgement".</summary>
public class ClaudeTasteJudgeTests
{
    private const string Key = "sample-test-value";

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

    private const string Dislikes = "{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"ingredient\",\"subject\":\"mushrooms\",\"stance\":\"dislikes\"}]}";

    private static readonly TasteEvidence Evidence = new(Sam, TasteEvidenceKind.Denial, "the mushrooms again, I cannot stand them", Risotto, Day(3));

    private static HttpResponseMessage Reply(string text, string? stop = null) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { stop_reason = stop ?? "end_turn", content = new[] { new { type = "text", text } } }), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Status(HttpStatusCode code, string body = "{}") => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (Judge Judge, Handler Handler, Mock<ICredentialHealthSink> Health) Make(Func<HttpRequestMessage, string, HttpResponseMessage> respond, string key = Key)
    {
        var handler = new Handler(respond);
        var health = new Mock<ICredentialHealthSink>();
        var options = Options.Create(new ClaudeVisionOptions { ApiKey = key, Model = "test-model" });
        return (new Judge(new HttpClient(handler), options, card => card == Risotto ? "Invented mushroom risotto" : null, health.Object), handler, health);
    }

    private static string UserText(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    [Fact]
    public async Task AGoodReplyYieldsTheLesson()
    {
        var (judge, _, health) = Make((_, _) => Reply(Dislikes));

        var lasting = Assert.IsType<TasteJudgement.Lasting>(await judge.JudgeAsync(Evidence, CancellationToken.None));

        Assert.Equal([new TasteLesson(TasteSubjectKind.Ingredient, "mushrooms", TasteStance.Dislikes)], lasting.Lessons);
        health.Verify(sink => sink.MarkOk(CredentialIds.Claude), Times.Once);
    }

    [Fact]
    public async Task OnlyTheDishNameAndTheReasonAreSent()
    {
        var (judge, handler, _) = Make((_, _) => Reply(Dislikes));

        await judge.JudgeAsync(Evidence, CancellationToken.None);

        Assert.Equal("Dish: Invented mushroom risotto\nReason: the mushrooms again, I cannot stand them", UserText(handler.Body));
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(["max_tokens", "messages", "model", "system"], body.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(TasteJudgePrompt.System, body.RootElement.GetProperty("system")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task NoRosterNameBirthdateOrIdIsInTheRequest()
    {
        var (judge, handler, _) = Make((_, _) => Reply(Dislikes));
        var household = new[] { "Sam", "Lee", "Robin", "Juniper", "birthdate", "birthday", "2014" };

        await judge.JudgeAsync(Evidence, CancellationToken.None);
        await judge.JudgeAsync(new TasteEvidence(Lee, TasteEvidenceKind.MarginNote, "too salty for the children", null, Day(4)), CancellationToken.None);

        foreach (var word in household)
        {
            Assert.DoesNotMatch(new Regex(@"\b" + word + @"\b", RegexOptions.IgnoreCase), handler.Body);
        }
    }

    [Fact]
    public async Task TheKeyIsOneHeaderAndNeverInTheBody()
    {
        var (judge, handler, _) = Make((_, _) => Reply(Dislikes));

        await judge.JudgeAsync(Evidence, CancellationToken.None);

        Assert.Equal(Key, Assert.Single(handler.Request!.Headers.GetValues("x-api-key")));
        Assert.DoesNotContain(Key, handler.Body, StringComparison.Ordinal);
        Assert.Equal("test-model", JsonDocument.Parse(handler.Body).RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task AnEvidenceWithNoCardSendsDishNone()
    {
        var (judge, handler, _) = Make((_, _) => Reply("{\"verdict\":\"oneoff\"}"));

        await judge.JudgeAsync(new TasteEvidence(Sam, TasteEvidenceKind.MarginNote, "needs more salt", null, Day(1)), CancellationToken.None);

        Assert.StartsWith("Dish: none\n", UserText(handler.Body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACardTheBoxNoLongerHoldsSendsDishNone()
    {
        var (judge, handler, _) = Make((_, _) => Reply("{\"verdict\":\"oneoff\"}"));

        await judge.JudgeAsync(Evidence with { Recipe = Pasta }, CancellationToken.None);

        Assert.StartsWith("Dish: none\n", UserText(handler.Body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoKeyIsTypedAndNothingIsSent()
    {
        var (judge, handler, _) = Make((_, _) => Reply(Dislikes), key: " ");

        var result = Assert.IsType<TasteJudgement.Unavailable>(await judge.JudgeAsync(Evidence, CancellationToken.None));

        Assert.Equal(AuthorUnavailable.NotConfigured, result.Reason);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARejectedKeyIsTypedAndReported(HttpStatusCode code)
    {
        var (judge, _, health) = Make((_, _) => Status(code));

        var result = Assert.IsType<TasteJudgement.Unavailable>(await judge.JudgeAsync(Evidence, CancellationToken.None));

        Assert.Equal(AuthorUnavailable.KeyRejected, result.Reason);
        health.Verify(sink => sink.MarkFailing(CredentialIds.Claude, It.IsAny<string>()), Times.Once);
        Assert.DoesNotContain(Key, result.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "503")]
    public async Task AServiceFailureNamesItsStatusAndTheServicesOwnReason(HttpStatusCode code, string number)
    {
        var (judge, _, health) = Make((_, _) => Status(code, "{\"error\":{\"message\":\"overloaded just now\"}}"));

        var result = Assert.IsType<TasteJudgement.Unavailable>(await judge.JudgeAsync(Evidence, CancellationToken.None));

        Assert.Equal(AuthorUnavailable.Offline, result.Reason);
        Assert.Contains(number, result.Detail, StringComparison.Ordinal);
        Assert.Contains("overloaded just now", result.Detail, StringComparison.Ordinal);
        health.Verify(sink => sink.MarkFailing(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        health.Verify(sink => sink.MarkOk(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ATransportFailureKeepsItsOwnStack()
    {
        var failure = new HttpRequestException("name resolution failed");
        var (judge, _, _) = Make((_, _) => throw failure);

        var result = Assert.IsType<TasteJudgement.Unavailable>(await judge.JudgeAsync(Evidence, CancellationToken.None));

        Assert.Equal(AuthorUnavailable.Offline, result.Reason);
        Assert.Same(failure, result.Cause);
        Assert.Contains(nameof(HttpRequestException), result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheClientsOwnTimeoutIsTimedOut()
    {
        var (judge, _, _) = Make((_, _) => throw new TaskCanceledException("the client gave up"));

        var result = Assert.IsType<TasteJudgement.Unavailable>(await judge.JudgeAsync(Evidence, CancellationToken.None));

        Assert.Equal(AuthorUnavailable.TimedOut, result.Reason);
    }

    [Fact]
    public async Task TheCallersCancellationPropagates()
    {
        var (judge, handler, _) = Make((_, _) => Reply(Dislikes));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => judge.JudgeAsync(Evidence, source.Token));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AMalformedReplyIsNoJudgementNeverAGuess()
    {
        var (judge, _, health) = Make((_, _) => Reply("Sure! They dislike mushrooms."));

        var result = Assert.IsType<TasteJudgement.Unavailable>(await judge.JudgeAsync(Evidence, CancellationToken.None));

        Assert.Equal(AuthorUnavailable.ReplyUnusable, result.Reason);
        health.Verify(sink => sink.MarkOk(CredentialIds.Claude), Times.Once);
    }

    [Fact]
    public async Task ARefusalIsNoJudgementEvenIfItsTextLooksLikeAnAnswer()
    {
        var (judge, _, _) = Make((_, _) => Reply(Dislikes, "refusal"));

        var result = Assert.IsType<TasteJudgement.Unavailable>(await judge.JudgeAsync(Evidence, CancellationToken.None));

        Assert.Equal(AuthorUnavailable.ReplyUnusable, result.Reason);
    }

    [Fact]
    public async Task AReplyWithNoTextBlockIsNoJudgement()
    {
        var (judge, _, _) = Make((_, _) => Status(HttpStatusCode.OK, "{\"content\":[]}"));

        Assert.Equal(AuthorUnavailable.ReplyUnusable, Assert.IsType<TasteJudgement.Unavailable>(await judge.JudgeAsync(Evidence, CancellationToken.None)).Reason);
    }

    [Fact]
    public async Task ABodyThatIsNotJsonIsNoJudgement()
    {
        var (judge, _, _) = Make((_, _) => Status(HttpStatusCode.OK, "<html>gateway</html>"));

        Assert.Equal(AuthorUnavailable.ReplyUnusable, Assert.IsType<TasteJudgement.Unavailable>(await judge.JudgeAsync(Evidence, CancellationToken.None)).Reason);
    }

    [Fact]
    public async Task ADishLessonIsAboutTheEvidencesCardEvenIfTheReplyNamesNoSubject()
    {
        var (judge, _, _) = Make((_, _) => Reply("{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"dish\",\"stance\":\"dislikes\"}]}"));

        var lasting = Assert.IsType<TasteJudgement.Lasting>(await judge.JudgeAsync(Evidence, CancellationToken.None));

        Assert.Equal(Risotto.Value, Assert.Single(lasting.Lessons).Subject);
    }
}
