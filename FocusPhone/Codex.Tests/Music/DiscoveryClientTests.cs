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
    public async Task FridayPicks_ParsesSelection()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """Here you go: {"title":"New for you","trackIds":["a"]}"""));

        var selection = await Client(handler).FridayPicksAsync(Profile(), Catalog, 12, note: null, CancellationToken.None);

        Assert.Equal("New for you", selection.Title);
        Assert.Equal(["a"], selection.TrackIds);
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
