using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class LrcLibClientTests
{
    private const string Url =
        "https://lrclib.net/api/get?artist_name=Switchfoot&track_name=Dare%20You%20To%20Move" +
        "&album_name=The%20Beautiful%20Letdown&duration=241";

    private static (LrcLibClient Client, FakeHttpMessageHandler Handler) Build()
    {
        var handler = new FakeHttpMessageHandler();
        return (new LrcLibClient(new HttpClient(handler)), handler);
    }

    private static Task<Codex.Domain.Music.Lyrics> Fetch(LrcLibClient client) =>
        client.FetchAsync(
            "Switchfoot", "Dare You To Move", "The Beautiful Letdown",
            TimeSpan.FromSeconds(241), CancellationToken.None);

    [Fact]
    public async Task FetchAsync_ReadsTimedLyrics()
    {
        var (client, handler) = Build();
        handler.Respond(Url, """
            {"id":1,"syncedLyrics":"[00:12.30]Welcome to the planet","plainLyrics":"Welcome to the planet"}
            """);

        var lyrics = await Fetch(client);

        Assert.True(lyrics.Timed);
        Assert.Equal("Welcome to the planet", lyrics.Lines.Single().Text);
    }

    [Fact]
    public async Task FetchAsync_FallsBackToPlainLyrics()
    {
        var (client, handler) = Build();
        handler.Respond(Url, """{"id":1,"syncedLyrics":null,"plainLyrics":"Welcome to the planet"}""");

        var lyrics = await Fetch(client);

        Assert.False(lyrics.Timed);
        Assert.Single(lyrics.Lines);
    }

    // The duration is what stops it returning a different recording of the same song;
    // the service matches on it.
    [Fact]
    public async Task FetchAsync_AsksForTheTrackItIsActuallyPlaying()
    {
        var (client, handler) = Build();
        handler.Respond(Url, "{}");

        await Fetch(client);

        Assert.Equal(Url, handler.Requests.Single().RequestUri!.AbsoluteUri);
    }

    // The service asks callers to identify themselves, and being a good guest of a
    // free, keyless service is the whole reason it stays usable.
    [Fact]
    public async Task FetchAsync_IdentifiesItself()
    {
        var (client, handler) = Build();
        handler.Respond(Url, "{}");

        await Fetch(client);

        Assert.NotEmpty(handler.Requests.Single().Headers.UserAgent);
    }

    // Most tracks have no lyrics on file. That is the ordinary case, not an error:
    // the panel says so and nothing else changes.
    [Fact]
    public async Task FetchAsync_ReturnsNothingWhenThereAreNone()
    {
        var (client, _) = Build();

        Assert.True((await Fetch(client)).IsEmpty);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"syncedLyrics":"","plainLyrics":""}""")]
    public async Task FetchAsync_ReturnsNothingForAReplyItCannotUse(string body)
    {
        var (client, handler) = Build();
        handler.Respond(Url, body);

        Assert.True((await Fetch(client)).IsEmpty);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNothingWithoutAnArtistOrTitle()
    {
        var (client, handler) = Build();

        Assert.True((await client.FetchAsync("", "", null, TimeSpan.Zero, CancellationToken.None)).IsEmpty);
        Assert.Empty(handler.Requests);
    }
}
