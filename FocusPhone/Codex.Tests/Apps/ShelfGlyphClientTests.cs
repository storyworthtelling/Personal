using System.Text.Json;
using Codex.Infrastructure.Apps;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;
using Xunit;

namespace Codex.Tests.Apps;

public class ShelfGlyphClientTests
{
    private const string Url = "https://api.anthropic.com/v1/messages";

    private static ShelfGlyphClient Client(FakeHttpMessageHandler handler, string apiKey = "sk-test") =>
        new(new HttpClient(handler), Options.Create(new VoiceOptions { ApiKey = apiKey }));

    private static string Reply(string text) =>
        JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } } });

    [Fact]
    public async Task GenerateAsync_ReturnsTheValidatedPath()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(Url, Reply("M10 10 L90 10 90 90 Z"));

        var path = await Client(handler).GenerateAsync("Reading", CancellationToken.None);

        Assert.Equal("M10 10 L90 10 90 90 Z", path);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsNull_WhenTheReplyIsNotAPath()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(Url, Reply("I can't draw that."));

        Assert.Null(await Client(handler).GenerateAsync("Reading", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_ReturnsNull_AndSkipsTheCall_WhenThereIsNoKey()
    {
        var handler = new FakeHttpMessageHandler();

        var path = await Client(handler, apiKey: "").GenerateAsync("Reading", CancellationToken.None);

        Assert.Null(path);
        Assert.Empty(handler.Requests); // never hit the network without a key
    }

    [Fact]
    public async Task GenerateAsync_ReturnsNull_OnAnHttpError()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(Url, "nope", status: System.Net.HttpStatusCode.InternalServerError);

        Assert.Null(await Client(handler).GenerateAsync("Reading", CancellationToken.None));
    }
}
