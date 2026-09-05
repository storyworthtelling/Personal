using System.Net;
using System.Text.Json;
using Codex.Infrastructure.Plate;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// The caption call. It is a pleasant extra on a screen full of the owner's own pictures, so
/// every one of these tests is really the same assertion from a different angle: it must
/// never be the reason the Darkroom fails to open.
/// </summary>
public class DarkroomCaptionClientTests
{
    private const string Url = "https://api.anthropic.com/v1/messages";

    private static (DarkroomCaptionClient Client, FakeHttpMessageHandler Handler) Build(
        string apiKey = "test-key")
    {
        var handler = new FakeHttpMessageHandler();
        var client = new DarkroomCaptionClient(
            new HttpClient(handler),
            Options.Create(new VoiceOptions { ApiKey = apiKey }));
        return (client, handler);
    }

    private static string Reply(string line) =>
        JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = line } } });

    private static IReadOnlyList<byte[]> OnePicture() => [[1, 2, 3]];

    [Fact]
    public async Task TheLineComesBackWithoutItsQuotes()
    {
        var (client, handler) = Build();
        handler.Respond(Url, Reply("  \"Four goes at the same tree.\"  "));

        Assert.Equal("Four goes at the same tree.",
            await client.CaptionAsync(OnePicture(), CancellationToken.None));
    }

    [Fact]
    public async Task NoKeyMeansNoCallAtAll()
    {
        // The vault may not have been unlocked yet. Asking anyway would fail slowly rather
        // than quickly, on the screen the owner just opened.
        var (client, handler) = Build(apiKey: "");

        Assert.Null(await client.CaptionAsync(OnePicture(), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NoPicturesMeansNoCallEither()
    {
        var (client, handler) = Build();

        Assert.Null(await client.CaptionAsync([], CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnyFailureIsJustNoLine(HttpStatusCode status)
    {
        var (client, handler) = Build();
        handler.Respond(Url, "{}", status: status);

        Assert.Null(await client.CaptionAsync(OnePicture(), CancellationToken.None));
    }

    [Fact]
    public async Task AReplyWithNoTextInItIsNoLine()
    {
        var (client, handler) = Build();
        handler.Respond(Url, JsonSerializer.Serialize(new { content = Array.Empty<object>() }));

        Assert.Null(await client.CaptionAsync(OnePicture(), CancellationToken.None));
    }

    [Fact]
    public async Task NonsenseInsteadOfJsonIsNoLine()
    {
        var (client, handler) = Build();
        handler.Respond(Url, "<html>a captive portal</html>");

        Assert.Null(await client.CaptionAsync(OnePicture(), CancellationToken.None));
    }

    [Fact]
    public async Task ThePicturesAreSentAsImageBlocksBeforeTheWords()
    {
        // Claude reads an image-then-text request best, and the block shape is the one the
        // Messages API documents: a base64 source with its media type.
        var (client, handler) = Build();
        handler.Respond(Url, Reply("A line."));

        await client.CaptionAsync(OnePicture(), CancellationToken.None);

        using var sent = JsonDocument.Parse(handler.RequestBodies[0]!);
        var content = sent.RootElement
            .GetProperty("messages")[0]
            .GetProperty("content");

        Assert.Equal("image", content[0].GetProperty("type").GetString());
        Assert.Equal("base64", content[0].GetProperty("source").GetProperty("type").GetString());
        Assert.Equal("image/jpeg",
            content[0].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal("text",
            content[content.GetArrayLength() - 1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task OnlyAHandfulOfPicturesEverGoOverTheRadio()
    {
        // They travel as base64 in the request body, so every extra one is real bytes on a
        // phone's mobile connection.
        var (client, handler) = Build();
        handler.Respond(Url, Reply("A line."));

        await client.CaptionAsync([[1], [2], [3], [4], [5], [6], [7]], CancellationToken.None);

        using var sent = JsonDocument.Parse(handler.RequestBodies[0]!);
        var images = sent.RootElement.GetProperty("messages")[0].GetProperty("content")
            .EnumerateArray()
            .Count(block => block.GetProperty("type").GetString() == "image");

        Assert.Equal(DarkroomCaptionClient.MostPictures, images);
    }

    [Fact]
    public async Task ItRunsOnTheCheapTierAndCarriesTheKey()
    {
        // A small, frequent call against a fixed voice is exactly what the fast tier is for,
        // and the key is the shared one out of the vault rather than a second key to manage.
        var (client, handler) = Build();
        handler.Respond(Url, Reply("A line."));

        await client.CaptionAsync(OnePicture(), CancellationToken.None);

        using var sent = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.Equal(new VoiceOptions().FastModel, sent.RootElement.GetProperty("model").GetString());
        Assert.Equal("test-key", handler.Requests[0].Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01",
            handler.Requests[0].Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task TheInstructionForbidsNamingAnyone()
    {
        // Claude refuses to identify people in images as a matter of policy, and asking it
        // to would be the wrong thing to ask regardless. Recognising a face happens on the
        // phone instead.
        var (client, handler) = Build();
        handler.Respond(Url, Reply("A line."));

        await client.CaptionAsync(OnePicture(), CancellationToken.None);

        using var sent = JsonDocument.Parse(handler.RequestBodies[0]!);
        var system = sent.RootElement.GetProperty("system").GetString()!;
        Assert.Contains("Do not name or identify any person", system, StringComparison.Ordinal);
    }
}
