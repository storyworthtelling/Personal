using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Foreword.Domain.Tasks;
using Foreword.Infrastructure.Handwriting;
using Foreword.Infrastructure.Tasks;
using Xunit;

namespace Foreword.Tests.Tasks;

/// <summary>
/// Reading the owner's sentence into a rule.
///
/// <para>The reply is Claude's, so the test cases are the shapes a reply actually arrives in — the
/// good one, the one wrapped in prose, the refusal, the outage. What matters most is what happens
/// when it <b>fails</b>: the sentence is kept as written, marked unparsed, and read again later. A
/// rule typed on a dead network has to become a rule without anyone retyping it.</para>
/// </summary>
public class TaskReadingTests
{
    private const string Lawn =
        "mow the lawn once this weekend, saturday if it's dry, needs about three dry hours, april to october";

    private static ClaudeTaskReader Reader(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)),
            Options.Create(new AnthropicOptions { ApiKey = "key", Model = "m" }),
            NullLogger<ClaudeTaskReader>.Instance);

    private static HttpResponseMessage Reply(string text) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"content":[{"type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(text)}}}]}"""),
        };

    [Fact]
    public async Task TheLawnRuleComesBackAsASmartTask()
    {
        var reader = Reader(_ => Reply("""
            {"title":"Mow the lawn","kind":"smart","window":"weekend","prefers":"Saturday",
             "dryHoursNeeded":3,"seasonFrom":4,"seasonTo":10,"ifNoDay":"printAnyway"}
            """));

        var task = await reader.Read(Lawn);

        Assert.NotNull(task);
        Assert.Equal("Mow the lawn", task!.Title);
        Assert.Equal(TaskKind.Smart, task.Kind);
        Assert.Equal(TaskWindow.Weekend, task.Window);
        Assert.Equal(DayOfWeek.Saturday, task.Prefers);
        Assert.Equal(3, task.DryHoursNeeded);
        Assert.Equal(new SeasonRange(4, 10), task.Season);
        Assert.Equal(NoDayFallback.PrintAnyway, task.IfNoDay);
        Assert.False(task.Unparsed);
    }

    [Fact]
    public async Task ADailyRuleComesBackAsARule()
    {
        var reader = Reader(_ => Reply("""{"title":"Vitamins","kind":"rule","repeat":"daily"}"""));

        var task = await reader.Read("vitamins every day");

        Assert.Equal(TaskKind.Rule, task!.Kind);
        Assert.Equal(Repeat.Daily, task.Repeat);
        Assert.True(task.Season.IsAllYear);
    }

    [Fact]
    public async Task AWeeklyRuleCarriesItsDay()
    {
        var reader = Reader(_ => Reply(
            """{"title":"Bins out","kind":"rule","repeat":"weekly","on":"Sunday"}"""));

        var task = await reader.Read("bins out every sunday night");

        Assert.Equal(Repeat.Weekly, task!.Repeat);
        Assert.Equal(DayOfWeek.Sunday, task.On);
    }

    /// <summary>
    /// Models wrap JSON in prose however firmly they are told not to. The object is found inside the
    /// reply rather than the reply being required to be one — a rule lost to a sentence of
    /// preamble would be a rule the owner has to type twice.
    /// </summary>
    [Fact]
    public async Task JsonWrappedInProseIsStillRead()
    {
        var reader = Reader(_ => Reply(
            "Here's the rule:\n```json\n{\"title\":\"Vitamins\",\"kind\":\"rule\",\"repeat\":\"daily\"}\n```\nHope that helps."));

        Assert.Equal("Vitamins", (await reader.Read("vitamins every day"))!.Title);
    }

    /// <summary>
    /// The sentence the owner typed is kept on the task whatever happens, so it can be read again
    /// later — and so the edit screen can offer "retype it in your own words" against what they
    /// actually wrote rather than against a title derived from it.
    /// </summary>
    [Fact]
    public async Task WhatTheyWroteIsKept()
    {
        var reader = Reader(_ => Reply("""{"title":"Mow the lawn","kind":"smart"}"""));

        Assert.Equal(Lawn, (await reader.Read(Lawn))!.Written);
    }

    // ---- the failures, which are the point ----

    [Fact]
    public async Task AnOutageReadsAsNothingRatherThanThrowing()
    {
        var reader = Reader(_ => throw new HttpRequestException("no network"));

        Assert.Null(await reader.Read(Lawn));
    }

    [Fact]
    public async Task ARejectedKeyReadsAsNothing()
    {
        var reader = Reader(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        Assert.Null(await reader.Read(Lawn));
    }

    [Fact]
    public async Task AReplyThatIsNotTheShapeAskedForReadsAsNothing()
    {
        var reader = Reader(_ => Reply("I'm afraid I can't help with that."));

        Assert.Null(await reader.Read(Lawn));
    }

    [Fact]
    public async Task WithNoKeyNothingIsCalled()
    {
        var reader = new ClaudeTaskReader(
            new HttpClient(new StubHandler(_ => throw new InvalidOperationException("must not call"))),
            Options.Create(new AnthropicOptions()),
            NullLogger<ClaudeTaskReader>.Instance);

        Assert.Null(await reader.Read(Lawn));
    }

    /// <summary>
    /// An empty sentence is not sent anywhere. There is nothing to read, and asking a model to read
    /// nothing spends a call to be told so.
    /// </summary>
    [Fact]
    public async Task AnEmptySentenceIsNotSent()
    {
        var reader = new ClaudeTaskReader(
            new HttpClient(new StubHandler(_ => throw new InvalidOperationException("must not call"))),
            Options.Create(new AnthropicOptions { ApiKey = "key" }),
            NullLogger<ClaudeTaskReader>.Instance);

        Assert.Null(await reader.Read("   "));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
