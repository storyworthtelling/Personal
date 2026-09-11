using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Foreword.Infrastructure.Brief;
using Xunit;

namespace Foreword.Tests.Nyt;

/// <summary>
/// The two keyless feeds, against the shapes a live response actually carries (F14, F15 — both
/// checked against the real endpoints while writing this).
/// </summary>
public class BriefExtrasClientTests
{
    private static XkcdClient Xkcd(Func<string, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)), NullLogger<XkcdClient>.Instance);

    private static WikipediaHistoryClient History(Func<string, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)), NullLogger<WikipediaHistoryClient>.Instance);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage Bytes(params byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    // The live shape, trimmed.
    private const string Strip = """
    {"month":"9","num":3297,"link":"","year":"2026","news":"","safe_title":"OH Scale",
     "transcript":"","alt":"To access distant parts of the project, I ended up building a regular-scale passenger train inside each rail.",
     "img":"https://imgs.xkcd.com/comics/oh_scale.png","title":"OH Scale","day":"11"}
    """;

    // ---- xkcd ----

    [Fact]
    public async Task TheLatestNumberComesBack()
    {
        Assert.Equal(3297, await Xkcd(_ => Json(Strip)).Latest());
    }

    [Fact]
    public async Task AStripCarriesItsTitleAndHoverText()
    {
        var strip = await Xkcd(url => url.EndsWith(".png", StringComparison.Ordinal)
            ? Bytes(1, 2, 3)
            : Json(Strip)).Get(3297);

        Assert.NotNull(strip);
        Assert.Equal("OH Scale", strip!.Title);
        Assert.StartsWith("To access distant parts", strip.AltText, StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], strip.Image);
    }

    /// <summary>
    /// A strip whose picture will not fetch is not a strip. Printing its title and hover text alone
    /// would read as a rendering fault rather than as a comic.
    /// </summary>
    [Fact]
    public async Task AStripWithNoPictureIsNoStrip()
    {
        var strip = await Xkcd(url => url.EndsWith(".png", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Json(Strip)).Get(3297);

        Assert.Null(strip);
    }

    /// <summary>
    /// Comic 404 is never asked for. The gap is the joke — the API has no endpoint for it and never
    /// will — and a request for it would return a 404 every morning forever.
    /// </summary>
    [Fact]
    public async Task TheMissingComicIsNotRequested()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call"));

        Assert.Null(await new XkcdClient(new HttpClient(handler), NullLogger<XkcdClient>.Instance).Get(404));
    }

    [Fact]
    public async Task AnOutageReadsAsNothingRatherThanThrowing()
    {
        Assert.Null(await Xkcd(_ => throw new HttpRequestException("down")).Latest());
        Assert.Null(await Xkcd(_ => throw new HttpRequestException("down")).Get(3297));
    }

    // ---- on this day ----

    [Fact]
    public async Task TheDaysEventsComeBack()
    {
        var notes = await History(_ => Json("""
            {"events":[
              {"text":"A resupply mission is launched.","year":2025},
              {"text":"The Battle of Stirling Bridge is fought.","year":1297}
            ]}
            """)).OnThisDay(new DateOnly(2026, 9, 11));

        Assert.Equal(2, notes.Count);
        Assert.Equal(1297, notes[1].Year);
    }

    /// <summary>
    /// The year arrives as a NUMBER in some entries and a STRING in others — the feed is built from
    /// wiki markup, so both shapes are real, and a reader that assumed one would silently drop half
    /// the day.
    /// </summary>
    [Fact]
    public async Task AYearGivenAsAStringIsReadToo()
    {
        var notes = await History(_ => Json(
            """{"events":[{"text":"Something happened.","year":"1297"}]}""")).OnThisDay(new DateOnly(2026, 9, 11));

        Assert.Equal(1297, notes.Single().Year);
    }

    /// <summary>
    /// The path is zero-padded. Asked as "9/1" the feed answers for a different day or not at all,
    /// and either way the factoid would quietly be wrong rather than missing.
    /// </summary>
    [Fact]
    public async Task TheDateIsZeroPaddedInThePath()
    {
        string? asked = null;
        await History(url => { asked = url; return Json("""{"events":[]}"""); })
            .OnThisDay(new DateOnly(2026, 9, 1));

        Assert.EndsWith("/09/01", asked, StringComparison.Ordinal);
    }

    /// <summary>
    /// Wikimedia asks every client to identify itself, and an unidentified one can be throttled or
    /// refused — which would read here as a day with no history in it.
    /// </summary>
    [Fact]
    public async Task TheRequestIdentifiesItself()
    {
        var handler = new StubHandler(_ => Json("""{"events":[]}"""));

        await new WikipediaHistoryClient(new HttpClient(handler),
            NullLogger<WikipediaHistoryClient>.Instance).OnThisDay(new DateOnly(2026, 9, 11));

        Assert.Contains("Foreword", handler.UserAgents.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedCallReadsAsNoHistory()
    {
        Assert.Empty(await History(_ => throw new HttpRequestException("down"))
            .OnThisDay(new DateOnly(2026, 9, 11)));
        Assert.Empty(await History(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests))
            .OnThisDay(new DateOnly(2026, 9, 11)));
    }

    private sealed class StubHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.TryGetValues("User-Agent", out var agent))
            {
                UserAgents.Add(string.Join(" ", agent));
            }

            return Task.FromResult(respond(request.RequestUri!.ToString()));
        }
    }
}
