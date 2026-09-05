using System.Text.Json;
using Codex.Infrastructure.Music;
using Codex.Tests.Support;
using Storyworthtelling.Voice;
using Xunit;

namespace Codex.Tests.Music;

public class HiddenTrackNamerTests
{
    private static readonly VoiceOptions Options = new() { ApiKey = "sk-test", Model = "claude-sonnet-4-6" };

    private static string MessageResponse(string text) =>
        JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } }, stop_reason = "end_turn" });

    private static HiddenTrackNamer Client(QueuedHttpMessageHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options));

    [Fact]
    public async Task NameAsync_ReturnsTheTitleWhenClaudeKnowsIt()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"found":true,"title":"Her Majesty"}"""));

        var result = await Client(handler).NameAsync(
            "Abbey Road", "The Beatles", "The End", CancellationToken.None);

        Assert.True(result.Found);
        Assert.Equal("Her Majesty", result.Title);
    }

    [Fact]
    public async Task NameAsync_SendsTheAlbumArtistAndHostTrack()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse("""{"found":false}"""));

        await Client(handler).NameAsync("Abbey Road", "The Beatles", "The End", CancellationToken.None);

        var body = handler.RequestBodies.Single();
        Assert.Contains("claude-sonnet-4-6", body);
        Assert.Contains("Abbey Road", body);
        Assert.Contains("The Beatles", body);
        Assert.Contains("The End", body);
        Assert.Equal("sk-test", handler.Requests.Single().Headers.GetValues("x-api-key").Single());
    }

    // The one answer that matters most to get right: a name Claude is not actually
    // confident of is worse than no name, so an honest "I don't know" must come back
    // as NotFound rather than the caller ever seeing a guessed title.
    [Fact]
    public async Task NameAsync_HonestlyUnknownComesBackNotFound()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse("""{"found":false}"""));

        var result = await Client(handler).NameAsync("Some Obscure EP", "Nobody Famous", "Track 4", CancellationToken.None);

        Assert.False(result.Found);
        Assert.Equal(string.Empty, result.Title);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"found":true}""")] // found:true but no title — half an answer, not a usable one
    [InlineData("""{"title":"   "}""")]
    public async Task NameAsync_TreatsAnyGarbledOrIncompleteReplyAsNotFound(string reply)
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(reply));

        var result = await Client(handler).NameAsync("Album", "Artist", "Track", CancellationToken.None);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task NameAsync_ATransportFailureIsNotFoundRatherThanAnException()
    {
        var handler = new QueuedHttpMessageHandler().Enqueue(System.Net.HttpStatusCode.InternalServerError, "boom");

        var result = await Client(handler).NameAsync("Album", "Artist", "Track", CancellationToken.None);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task NameAsync_ReadsPastLeadingProseAroundTheJson()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """Yes, I recognize this one: {"found":true,"title":"Endless, Nameless"}"""));

        var result = await Client(handler).NameAsync("Nevermind", "Nirvana", "Something in the Way", CancellationToken.None);

        Assert.True(result.Found);
        Assert.Equal("Endless, Nameless", result.Title);
    }
}
