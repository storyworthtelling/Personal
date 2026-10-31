using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Constitution.Domain.Credentials;
using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Workouts;
using Microsoft.Extensions.Options;
using Moq;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Voice.Health;
using Author = Constitution.Infrastructure.Meals.ClaudeTemplateAuthor;
using static Constitution.Tests.Templates.TemplateData;

namespace Constitution.Tests.ClaudeTemplateAuthor;

/// <summary>The real template author over the Messages API against a stand-in: only words leave, the reply is read by the committed parser, and every failure is a typed reason.</summary>
public class ClaudeTemplateAuthorTests
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

    private const string GoodReply =
        "{\"templates\":[{\"slot\":\"weekend-breakfast\",\"words\":\"Fruit salad and toast\"},{\"slot\":\"box-fruit\",\"words\":\"Pear slices\"}]}";

    private static readonly TemplateRequest Ask = new(
        3,
        [MealTemplateSlot.WeekendBreakfast, MealTemplateSlot.BoxFruit],
        [new TemplateKnown(MealTemplateSlot.WeekendBreakfast, "Oat pancakes")],
        [new TemplateKnown(MealTemplateSlot.BoxFruit, "Cold melon")],
        [new TemplateKnown(MealTemplateSlot.BoxFruit, "Kiwi halves")],
        [new TemplateTasteHint("cuisine", "thai", "likes")]);

    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } } }), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Status(HttpStatusCode code, string body = "{}") =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (Author Author, Handler Handler, Mock<ICredentialHealthSink> Health) Make(
        Func<HttpRequestMessage, string, HttpResponseMessage> respond, string key = "sample-test-value")
    {
        var handler = new Handler(respond);
        var health = new Mock<ICredentialHealthSink>();
        var options = Options.Create(new ClaudeVisionOptions { ApiKey = key, Model = "test-model" });
        return (new Author(new HttpClient(handler), options, health.Object), handler, health);
    }

    private static string UserText(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    [Fact]
    public async Task AGoodReplyYieldsProposals()
    {
        var (author, _, health) = Make((_, _) => Reply(GoodReply));

        var result = await author.ProposeAsync(Ask, CancellationToken.None);

        var drafts = Assert.IsType<TemplateAuthored.Drafts>(result);
        Assert.Equal([new TemplateDraft(MealTemplateSlot.WeekendBreakfast, "Fruit salad and toast"), new TemplateDraft(MealTemplateSlot.BoxFruit, "Pear slices")], drafts.Proposals);
        health.Verify(sink => sink.MarkOk(CredentialIds.Claude), Times.Once);
    }

    [Fact]
    public async Task TheRequestIsTextOnlyWithTheFixedSystemPrompt()
    {
        var (author, handler, _) = Make((_, _) => Reply(GoodReply));

        await author.ProposeAsync(Ask, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body);
        var root = body.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.Equal(Author.SystemPrompt, root.GetProperty("system")[0].GetProperty("text").GetString());
        var content = root.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal(["max_tokens", "messages", "model", "system"], root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.DoesNotContain("\"image\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSystemPromptHasNoDigitsAndNoPersonalWords()
    {
        Assert.DoesNotMatch(@"\d", Author.SystemPrompt);
        Assert.Contains("templates", Author.SystemPrompt, StringComparison.Ordinal);
        foreach (var name in new[] { "Sam", "Lee", "Kids" })
        {
            Assert.DoesNotContain(name, Author.SystemPrompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheKeyIsInTheHeaderOnly()
    {
        var (author, handler, _) = Make((_, _) => Reply(GoodReply));

        await author.ProposeAsync(Ask, CancellationToken.None);

        Assert.Equal("sample-test-value", Assert.Single(handler.Request!.Headers.GetValues("x-api-key")));
        Assert.DoesNotContain("sample-test-value", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyTheAllowedWordsLeaveThePhone()
    {
        var reason = "Sam could not stand the pith";
        var library = Lib(
            Approved("tpl-weekend-breakfast-oats", MealTemplateSlot.WeekendBreakfast, "Oat pancakes"),
            Rejected("tpl-box-fruit-melon", MealTemplateSlot.BoxFruit, "Cold melon", reason),
            Proposed("tpl-box-fruit-kiwi", MealTemplateSlot.BoxFruit, "Kiwi halves"));
        var notes = new[]
        {
            new TasteNote(new PersonId("sam"), TasteSubjectKind.Cuisine, "italian", TasteStance.Likes, TasteOrigin.Learned, new DateOnly(2026, 10, 1)),
            new TasteNote(new PersonId("lee"), TasteSubjectKind.Ingredient, "olives", TasteStance.Dislikes, TasteOrigin.Seeded, new DateOnly(2026, 10, 2)),
            new TasteNote(new PersonId("sam"), TasteSubjectKind.Dish, "card-lasagne-bake", TasteStance.Likes, TasteOrigin.Learned, new DateOnly(2026, 10, 3)),
        };
        var request = TemplateRequest.From(library, notes, [MealTemplateSlot.WeekendBreakfast, MealTemplateSlot.BoxFruit], 3);
        var (author, handler, _) = Make((_, _) => Reply(GoodReply));

        await author.ProposeAsync(request, CancellationToken.None);

        var text = UserText(handler.Body);
        foreach (var allowed in new[] { "Oat pancakes", "Cold melon", "Kiwi halves", "italian", "olives", "likes", "dislikes", "weekend-breakfast", "box-fruit" })
        {
            Assert.Contains(allowed, text, StringComparison.Ordinal);
        }

        foreach (var forbidden in new[] { "Sam", "Lee", "sam", "lee", "pith", reason, "tpl-", "card-", "lasagne", "2026" })
        {
            Assert.DoesNotContain(forbidden, handler.Body, StringComparison.Ordinal);
        }

        Assert.Contains("Slots needing lines: weekend-breakfast, box-fruit\n", text.Replace("\r", string.Empty), StringComparison.Ordinal);

        // Digits: the asked count, once, and nothing else.
        Assert.Equal("3", Regex.Replace(text, @"\D", string.Empty));
        Assert.DoesNotMatch(@"\d", Author.SystemPrompt);
    }

    [Fact]
    public async Task LongListsAreBounded()
    {
        var many = Enumerable.Range(0, 80).Select(number => new TemplateKnown(MealTemplateSlot.BoxFruit, "Fruit idea " + new string((char)('a' + (number % 26)), 1 + (number / 26)))).ToList();
        var request = Ask with { Approved = many, Rejected = many, Pending = many };
        var (author, handler, _) = Make((_, _) => Reply(GoodReply));

        await author.ProposeAsync(request, CancellationToken.None);

        var lines = UserText(handler.Body).Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal));
        Assert.True(lines <= Author.MostLines, $"{lines} lines went out");
        Assert.True(lines > 0);
    }

    [Fact]
    public async Task LongTasteListsAreBounded()
    {
        var tastes = Enumerable.Range(0, 80).Select(number => new TemplateTasteHint("ingredient", "food" + new string((char)('a' + (number % 26)), 1 + (number / 26)), "likes")).ToList();
        var (author, handler, _) = Make((_, _) => Reply(GoodReply));

        await author.ProposeAsync(Ask with { Tastes = tastes }, CancellationToken.None);

        var lines = UserText(handler.Body).Split('\n').Count(line => line.StartsWith("- ingredient", StringComparison.Ordinal));
        Assert.Equal(Author.MostTastes, lines);
    }

    [Fact]
    public async Task TheCountAskedForIsTheCountAtMost()
    {
        var reply = "{\"templates\":[{\"slot\":\"box-fruit\",\"words\":\"Pear\"},{\"slot\":\"box-fruit\",\"words\":\"Plum\"},{\"slot\":\"box-fruit\",\"words\":\"Grapes\"},{\"slot\":\"box-fruit\",\"words\":\"Apple\"}]}";
        var (author, _, _) = Make((_, _) => Reply(reply));

        var result = await author.ProposeAsync(Ask with { Count = 2 }, CancellationToken.None);

        Assert.Equal(2, Assert.IsType<TemplateAuthored.Drafts>(result).Proposals.Count);
    }

    [Fact]
    public async Task ASlotThatWasNotAskedForIsDropped()
    {
        var reply = "{\"templates\":[{\"slot\":\"weekday-lunch\",\"words\":\"Soup\"},{\"slot\":\"box-fruit\",\"words\":\"Pear\"}]}";
        var (author, _, _) = Make((_, _) => Reply(reply));

        var result = await author.ProposeAsync(Ask, CancellationToken.None);

        Assert.Equal([new TemplateDraft(MealTemplateSlot.BoxFruit, "Pear")], Assert.IsType<TemplateAuthored.Drafts>(result).Proposals);
    }

    [Fact]
    public async Task NothingTheReplySaysMakesAnythingApproved()
    {
        var reply = "{\"templates\":[{\"slot\":\"box-fruit\",\"words\":\"Pear\",\"status\":\"approved\",\"approval\":\"explicit\"}]}";
        var (author, _, _) = Make((_, _) => Reply(reply));

        var result = await author.ProposeAsync(Ask, CancellationToken.None);

        var merged = TemplateProposals.Merge(new TemplateLibrary([], []), Assert.IsType<TemplateAuthored.Drafts>(result).Proposals);
        var added = Assert.Single(merged.Added);
        Assert.Equal(TemplateStatus.Proposed, added.Status);
        Assert.Equal(TemplateApproval.None, added.Approval);
    }

    [Theory]
    [InlineData("I'm sorry, I can't help with that.")]
    [InlineData("{\"templates\":[")]
    [InlineData("{\"templates\":[]}")]
    [InlineData("{\"templates\":[{\"slot\":\"box-fruit\"}]}")]
    [InlineData("{\"templates\":[{\"slot\":\"box-fruit\",\"words\":\"Oat pancakes\"}]}")]
    [InlineData("")]
    public async Task AnUnusableReplyProposesNothing(string text)
    {
        var (author, _, _) = Make((_, _) => Reply(text));

        var result = await author.ProposeAsync(Ask, CancellationToken.None);

        Assert.Equal(AuthorUnavailable.ReplyUnusable, Assert.IsType<TemplateAuthored.Unavailable>(result).Reason);
    }

    [Fact]
    public async Task AnOversizedReplyProposesNothing()
    {
        var long1 = "{\"templates\":[{\"slot\":\"box-fruit\",\"words\":\"Pear\"}]}" + new string(' ', TemplateProposalParser.MaxReplyLength);
        var (author, _, _) = Make((_, _) => Reply(long1));

        var result = await author.ProposeAsync(Ask, CancellationToken.None);

        Assert.Equal(AuthorUnavailable.ReplyUnusable, Assert.IsType<TemplateAuthored.Unavailable>(result).Reason);
    }

    [Fact]
    public async Task ABodyThatIsNotTheServiceShapeIsUnusable()
    {
        var (author, _, _) = Make((_, _) => Status(HttpStatusCode.OK, "<html>captive portal</html>"));

        var result = await author.ProposeAsync(Ask, CancellationToken.None);

        Assert.Equal(AuthorUnavailable.ReplyUnusable, Assert.IsType<TemplateAuthored.Unavailable>(result).Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARejectedKeyIsFlaggedAndNamed(HttpStatusCode code)
    {
        var (author, _, health) = Make((_, _) => Status(code, "{\"error\":{\"message\":\"invalid x-api-key\"}}"));

        var result = await author.ProposeAsync(Ask, CancellationToken.None);

        Assert.Equal(AuthorUnavailable.KeyRejected, Assert.IsType<TemplateAuthored.Unavailable>(result).Reason);
        health.Verify(sink => sink.MarkFailing(CredentialIds.Claude, It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task AServiceFailureIsOfflineAndDoesNotFlagTheKey(HttpStatusCode code)
    {
        var (author, _, health) = Make((_, _) => Status(code));

        var result = await author.ProposeAsync(Ask, CancellationToken.None);

        Assert.Equal(AuthorUnavailable.Offline, Assert.IsType<TemplateAuthored.Unavailable>(result).Reason);
        health.Verify(sink => sink.MarkFailing(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ANetworkFailureIsOffline()
    {
        var (author, _, health) = Make((_, _) => throw new HttpRequestException("no route"));

        var result = await author.ProposeAsync(Ask, CancellationToken.None);

        Assert.Equal(AuthorUnavailable.Offline, Assert.IsType<TemplateAuthored.Unavailable>(result).Reason);
        health.Verify(sink => sink.MarkFailing(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ATimeoutIsTimedOut()
    {
        var (author, _, _) = Make((_, _) => throw new TaskCanceledException("the client timed out"));

        var result = await author.ProposeAsync(Ask, CancellationToken.None);

        Assert.Equal(AuthorUnavailable.TimedOut, Assert.IsType<TemplateAuthored.Unavailable>(result).Reason);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var (author, _, _) = Make((_, _) => throw new TaskCanceledException("cancelled"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => author.ProposeAsync(Ask, source.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoKeyIsNotConfiguredAndNothingIsSent(string key)
    {
        var (author, handler, health) = Make((_, _) => Reply(GoodReply), key);

        var result = await author.ProposeAsync(Ask, CancellationToken.None);

        Assert.Equal(AuthorUnavailable.NotConfigured, Assert.IsType<TemplateAuthored.Unavailable>(result).Reason);
        Assert.Equal(0, handler.Calls);
        health.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TheKeyIsReadWhenAsked()
    {
        var options = new ClaudeVisionOptions { ApiKey = string.Empty, Model = "test-model" };
        var handler = new Handler((_, _) => Reply(GoodReply));
        var author = new Author(new HttpClient(handler), Options.Create(options));

        Assert.IsType<TemplateAuthored.Unavailable>(await author.ProposeAsync(Ask, CancellationToken.None));
        options.ApiKey = "sample-test-value";
        Assert.IsType<TemplateAuthored.Drafts>(await author.ProposeAsync(Ask, CancellationToken.None));
    }
}
