using System.Text.Json;
using Microsoft.Extensions.Options;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Tests.Support;
using Storyworthtelling.Voice;
using Xunit;

namespace Codex.Tests.Music;

public class DiscoveryClientCountsTests
{
    private static readonly VoiceOptions Options = new() { ApiKey = "sk-test", Model = "claude-sonnet-5-5" };

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

    // The two counts reach the model separately, so it can be asked for, say, ten songs
    // and four shows rather than a single total it is free to fill however it likes.
    [Fact]
    public async Task WeeklyDiscoveries_AsksForEachKindByItsOwnCount()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse("""{"picks":[]}"""));

        await Client(handler).WeeklyDiscoveriesAsync(
            Profile(), Catalog, music: 10, podcasts: 4, note: null, subscribedShows: [], CancellationToken.None);

        var body = handler.RequestBodies.Single();
        Assert.Contains("MUSIC PICKS: 10", body);
        Assert.Contains("PODCAST PICKS: 4", body);
    }

    // The counts are honoured here as well as asked for in the instruction, the same way
    // the exclusion is: a model that over-delivers must not overfill the week.
    [Fact]
    public async Task WeeklyDiscoveries_TrimsAnOverDeliveredKindToItsCount()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"picks":[{"title":"One","artist":"X","kind":"song"},{"title":"Two","artist":"X","kind":"song"},{"title":"Three","artist":"X","kind":"song"},{"title":"Show","artist":"","kind":"podcast"}]}"""));

        var picks = await Client(handler).WeeklyDiscoveriesAsync(
            Profile(), Catalog, music: 2, podcasts: 1, note: null, subscribedShows: [], CancellationToken.None);

        Assert.Equal(["One", "Two", "Show"], picks.Select(pick => pick.Title));
    }

    [Fact]
    public async Task WeeklyDiscoveries_ZeroPodcastsAsksForNoShows()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"picks":[{"title":"Show","artist":"","kind":"podcast"}]}"""));

        var picks = await Client(handler).WeeklyDiscoveriesAsync(
            Profile(), Catalog, music: 5, podcasts: 0, note: null, subscribedShows: [], CancellationToken.None);

        Assert.Empty(picks);
    }
}
