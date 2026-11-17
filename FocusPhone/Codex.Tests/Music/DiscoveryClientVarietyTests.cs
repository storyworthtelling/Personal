using System.Net;
using System.Text.Json;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Tests.Support;
using Storyworthtelling.Voice;
using Xunit;

namespace Codex.Tests.Music;

/// <summary>
/// The owner's report: near-identical picks every Friday, and nothing new. The request was the
/// same every week (same profile, same library, no date, no memory), and the model cannot know a
/// record released after its training — so the crate now carries the date and what it has
/// already offered, and is given web search to find this season's releases.
/// </summary>
public class DiscoveryClientVarietyTests
{
    private static readonly VoiceOptions Options = new() { ApiKey = "sk-test", Model = "claude-sonnet-5-5" };
    private static readonly DateOnly Friday = new(2026, 11, 13);

    private static string MessageResponse(string text) =>
        JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } }, stop_reason = "end_turn" });

    private static DiscoveryClient Client(QueuedHttpMessageHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options));

    private static readonly IReadOnlyList<Track> Catalog =
    [
        new("a", "Peace Piece", "Bill Evans", "Album", "Bill Evans", 1, 1, "Jazz", TimeSpan.FromMinutes(6)),
    ];

    private static TasteProfile Profile() =>
        new("Leans jazz at night.", ["bebop"], [], ["hype"], ["Jazz"], ["Bill Evans"], ["nights"], false, 10);

    private static readonly CrateHistory LastWeeks =
        CrateHistory.Empty.Remember([new Recommendation("Blue in Green", "Bill Evans", "adjacent")], Friday.AddDays(-7));

    private static Task<IReadOnlyList<Recommendation>> Ask(QueuedHttpMessageHandler handler) =>
        Client(handler).WeeklyDiscoveriesAsync(
            Profile(), Catalog, music: 10, podcasts: 0, note: null, subscribedShows: [], LastWeeks, Friday, CancellationToken.None);

    [Fact]
    public async Task TheRequestCarriesTheDateAndWhatWasAlreadyOffered()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse("""{"picks":[]}"""));

        await Ask(handler);

        var body = handler.RequestBodies.Single();
        Assert.Contains("2026-11-13", body);
        Assert.Contains("Blue in Green", body);
    }

    [Fact]
    public async Task TheRequestOffersWebSearchForNewReleases()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse("""{"picks":[]}"""));

        await Ask(handler);

        using var body = JsonDocument.Parse(handler.RequestBodies.Single());
        var tool = body.RootElement.GetProperty("tools").EnumerateArray().Single();
        Assert.Equal("web_search", tool.GetProperty("name").GetString());
        Assert.Contains("new release", handler.RequestBodies.Single(), StringComparison.OrdinalIgnoreCase);
    }

    // Asked for and enforced, like the library exclusion: a model told not to repeat itself
    // will still sometimes do it.
    [Fact]
    public async Task APickOfferedInARecentWeekIsDropped()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"picks":[{"title":"Blue in Green","artist":"Bill Evans","why":"again"},{"title":"So What","artist":"Miles Davis","why":"new to you"}]}"""));

        var picks = await Ask(handler);

        Assert.Equal("So What", Assert.Single(picks).Title);
    }

    // An account without web search turned on refuses the tool with a 400. The crate is
    // worth more than the search, so it is asked again without it.
    [Fact]
    public async Task ARefusedSearchIsAskedAgainWithoutIt()
    {
        var handler = new QueuedHttpMessageHandler()
            .Enqueue(HttpStatusCode.BadRequest, """{"type":"error","error":{"type":"invalid_request_error","message":"web search is not enabled"}}""")
            .EnqueueOk(MessageResponse("""{"picks":[{"title":"So What","artist":"Miles Davis","why":"new to you"}]}"""));

        var picks = await Ask(handler);

        Assert.Equal("So What", Assert.Single(picks).Title);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.False(JsonDocument.Parse(handler.RequestBodies[1]).RootElement.TryGetProperty("tools", out _));
    }

    // A search that runs long can end the turn before the answer is written (pause_turn).
    // That reply holds no crate, so the crate is asked for again without searching.
    [Fact]
    public async Task AReplyWithNoCrateAfterSearchingIsAskedAgainWithoutIt()
    {
        var handler = new QueuedHttpMessageHandler()
            .EnqueueOk(JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = "Searching…" } }, stop_reason = "pause_turn" }))
            .EnqueueOk(MessageResponse("""{"picks":[{"title":"So What","artist":"Miles Davis","why":"new to you"}]}"""));

        var picks = await Ask(handler);

        Assert.Equal("So What", Assert.Single(picks).Title);
    }
}
