using System.Text.Json;
using Microsoft.Extensions.Options;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Tests.Support;
using Storyworthtelling.Voice;
using Xunit;

namespace Codex.Tests.Music;

public class DiscoveryClientTests
{
    private static readonly VoiceOptions Options = new() { ApiKey = "sk-test", Model = "claude-sonnet-4-6" };

    private static string MessageResponse(string text) =>
        JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } }, stop_reason = "end_turn" });

    private static DiscoveryClient Client(QueuedHttpMessageHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options));

    private static readonly IReadOnlyList<Track> Catalog =
    [
        new("a", "Peace Piece", "Bill Evans", "Album", "Bill Evans", 1, 1, "Jazz", TimeSpan.FromMinutes(6)),
        new("b", "So What", "Miles Davis", "Kind of Blue", "Miles Davis", 1, 1, "Jazz", TimeSpan.FromMinutes(9)),
    ];

    private static TasteProfile Profile() =>
        new("Leans jazz at night.", ["bebop"], [], ["hype"], ["Jazz"], ["Bill Evans"], ["nights"], false, 10);

    [Fact]
    public async Task GeneratePlaylist_ReturnsTitleAndIds_SendingProfileAndCatalog()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"title":"Late Night Jazz","trackIds":["b","a"]}"""));

        var selection = await Client(handler).GeneratePlaylistAsync(
            Profile(), Catalog, new PlaylistRequest("something mellow", 10), CancellationToken.None);

        Assert.Equal("Late Night Jazz", selection.Title);
        Assert.Equal(["b", "a"], selection.TrackIds);

        var body = handler.RequestBodies.Single();
        Assert.Contains("claude-sonnet-4-6", body);
        Assert.Contains("something mellow", body); // the request seed reached the model
        Assert.Contains("Peace Piece", body);      // the catalog digest reached the model
        Assert.Equal("sk-test", handler.Requests.Single().Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task WeeklyDiscoveries_ParsesNamedPicksWithTheirReasons()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """Here you go: {"picks":[{"title":"Blue in Green","artist":"Bill Evans","why":"the quiet end of what you play"}]}"""));

        var picks = await Client(handler).WeeklyDiscoveriesAsync(
            Profile(), Catalog, 12, note: null, subscribedShows: [], CancellationToken.None);

        var pick = Assert.Single(picks);
        Assert.Equal("Blue in Green", pick.Title);
        Assert.Equal("Bill Evans", pick.Artist);
        Assert.Equal("the quiet end of what you play", pick.Why);
    }

    // The crate is records the listener does NOT have. The instruction says so, and
    // this is the part that does not depend on the model having listened.
    [Fact]
    public async Task WeeklyDiscoveries_DropsAPickTheLibraryAlreadyHas()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"picks":[{"title":"So What","artist":"Miles Davis","why":"you would like it"},{"title":"Blue in Green","artist":"Bill Evans","why":"adjacent"}]}"""));

        var picks = await Client(handler).WeeklyDiscoveriesAsync(
            Profile(), Catalog, 12, note: null, subscribedShows: [], CancellationToken.None);

        Assert.Equal("Blue in Green", Assert.Single(picks).Title);
    }

    [Fact]
    public async Task WeeklyDiscoveries_TellsTheModelTheLibraryIsWhatToAvoid()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse("""{"picks":[]}"""));

        await Client(handler).WeeklyDiscoveriesAsync(
            Profile(), Catalog, 12, note: null, subscribedShows: ["Radiolab"], CancellationToken.None);

        var body = handler.RequestBodies.Single();
        Assert.Contains("do NOT already have", body);
        Assert.Contains("AVOID recommending", body);
        Assert.Contains("Peace Piece", body); // the library reached the model as evidence
        Assert.Contains("Radiolab", body); // the followed shows reached the model as evidence too
    }

    [Fact]
    public async Task WeeklyDiscoveries_OnAGarbledReply_IsEmptyRatherThanThrowing()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse("no json"));

        Assert.Empty(await Client(handler).WeeklyDiscoveriesAsync(
            Profile(), Catalog, 12, note: null, subscribedShows: [], CancellationToken.None));
    }

    [Fact]
    public async Task WeeklyDiscoveries_ParsesEachPicksKind()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"picks":[{"title":"Sketches of Spain","artist":"Miles Davis","why":"the whole record","kind":"album"},{"title":"Reply All","artist":"Gimlet","why":"you like tech stories","kind":"podcast"},{"title":"Blue in Green","artist":"Bill Evans","why":"adjacent","kind":"song"}]}"""));

        var picks = await Client(handler).WeeklyDiscoveriesAsync(
            Profile(), Catalog, 12, note: null, subscribedShows: [], CancellationToken.None);

        Assert.Equal(DiscoveryKind.Album, picks.Single(pick => pick.Title == "Sketches of Spain").Kind);
        Assert.Equal(DiscoveryKind.Podcast, picks.Single(pick => pick.Title == "Reply All").Kind);
        Assert.Equal(DiscoveryKind.Song, picks.Single(pick => pick.Title == "Blue in Green").Kind);
    }

    // An unrecognized or missing kind degrades to Song rather than dropping the pick — the
    // same "never wedge the crate over a garbled field" bargain every other field here takes.
    [Fact]
    public async Task WeeklyDiscoveries_DefaultsAnUnrecognizedKindToSong()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"picks":[{"title":"Blue in Green","artist":"Bill Evans","why":"adjacent","kind":"vinyl"}]}"""));

        var pick = Assert.Single(await Client(handler).WeeklyDiscoveriesAsync(
            Profile(), Catalog, 12, note: null, subscribedShows: [], CancellationToken.None));

        Assert.Equal(DiscoveryKind.Song, pick.Kind);
    }

    [Fact]
    public async Task WeeklyDiscoveries_DropsAPodcastPickAlreadyFollowed()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"picks":[{"title":"Radiolab","artist":"","why":"you like this","kind":"podcast"}]}"""));

        var picks = await Client(handler).WeeklyDiscoveriesAsync(
            Profile(), Catalog, 12, note: null, subscribedShows: ["Radiolab"], CancellationToken.None);

        Assert.Empty(picks);
    }

    [Fact]
    public async Task GarbledReply_ReturnsEmptySelection()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse("no json"));
        var selection = await Client(handler).GeneratePlaylistAsync(
            Profile(), Catalog, new PlaylistRequest("x"), CancellationToken.None);
        Assert.Same(DiscoverySelection.Empty, selection);
    }
}
