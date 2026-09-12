using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Foreword.Domain.Prayer;
using Foreword.Infrastructure.Handwriting;
using Foreword.Infrastructure.Prayer;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// The prayer page's own line — the fourth of the morning's four Claude calls, and the only one
/// that may see prayer content.
///
/// <para>Four separate calls is what makes the isolation structural rather than careful. This one
/// takes a <see cref="PastoralBrief"/>; the other three take strings and could not be handed
/// this if someone tried.</para>
/// </summary>
public class PastoralComposerTests
{
    private sealed class Reader : IPrayerReader
    {
    }

    private static string Read(PrayerText text) => text.ReadBy(new Reader());

    private static ClaudePastoralComposer Composer(
        Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)),
            Options.Create(new AnthropicOptions { ApiKey = "key", Model = "m" }),
            NullLogger<ClaudePastoralComposer>.Instance);

    private static HttpResponseMessage Reply(string text) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"content":[{"type":"text","text":{{JsonSerializer.Serialize(text)}}}]}"""),
        };

    private static PastoralBrief Brief() => new()
    {
        Held = 4,
        AnsweredThisMorning = 1,
        LongestHeld = "held 6 weeks",
        MostRecentEntry = PrayerText.Of("Asked for patience with the mornings."),
    };

    [Fact]
    public async Task ItComposesOneLine()
    {
        var composer = Composer(_ =>
            Reply("A short list this morning, and one of them can come off it."));

        var line = await composer.Compose(Brief(), CancellationToken.None);

        Assert.Equal("A short list this morning, and one of them can come off it.", Read(line));
    }

    /// <summary>
    /// It has to see the content to do its job, and that is the accepted limit of "private" — the
    /// page is read by a model. What matters is that this is the <b>only</b> composer it reaches.
    /// </summary>
    [Fact]
    public async Task ThePromptCarriesTheStateOfTheList()
    {
        string? body = null;

        var composer = Composer(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Reply("A line.");
        });

        await composer.Compose(Brief(), CancellationToken.None);

        Assert.NotNull(body);
        Assert.Contains("held 6 weeks", body!);
        Assert.Contains("patience", body!);
    }

    /// <summary>
    /// Empty when the call does not come back — the page simply has no line, rather than a neutral
    /// sentence pretending to be one. The same rule the rest of the morning's voice follows.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AFailedCallLeavesThePageWithNoLine(HttpStatusCode status)
    {
        var composer = Composer(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("{}"),
        });

        var line = await composer.Compose(Brief(), CancellationToken.None);

        Assert.True(line.IsEmpty);
    }

    [Fact]
    public async Task AReplyWithNoTextLeavesThePageWithNoLine()
    {
        var composer = Composer(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"content":[]}"""),
        });

        Assert.True((await composer.Compose(Brief(), CancellationToken.None)).IsEmpty);
    }

    /// <summary>
    /// A first morning has nothing to say anything about, so nothing is asked. Composing a pastoral
    /// line about an empty list is a call that costs money to produce a sentence about nothing.
    /// </summary>
    [Fact]
    public async Task AnEmptyMorningIsNotEvenAsked()
    {
        var composer = Composer(_ => throw new InvalidOperationException("must not call"));

        var line = await composer.Compose(
            new PastoralBrief { Held = 0, AnsweredThisMorning = 0 }, CancellationToken.None);

        Assert.True(line.IsEmpty);
    }

    /// <summary>
    /// A reply that runs long is trimmed to one line. The page has room for a sentence, and a
    /// paragraph in the calligraphy hand at Title size would run over the list it is introducing.
    /// </summary>
    [Fact]
    public async Task AReplyThatRunsOnIsCutToItsFirstLine()
    {
        var composer = Composer(_ => Reply(
            "A short list this morning.\n\nAlso, here is some more commentary you did not ask for."));

        var line = await composer.Compose(Brief(), CancellationToken.None);

        Assert.Equal("A short list this morning.", Read(line));
    }

    [Fact]
    public void TheComposerIsADeclaredReader()
    {
        Assert.IsAssignableFrom<IPrayerReader>(Composer(_ => Reply("x")));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
