using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class DeezerTempoClientTests
{
    private const string SearchUrl =
        "https://api.deezer.com/search?q=artist%3A%22Switchfoot%22%20track%3A%22Dare%20You%20To%20Move%22";

    private const string Found = """
        {"data":[{"id":3135556,"title":"Dare You To Move","artist":{"name":"Switchfoot"}}],"total":1}
        """;

    private static (DeezerTempoClient Client, FakeHttpMessageHandler Handler) Build()
    {
        var handler = new FakeHttpMessageHandler();
        return (new DeezerTempoClient(new HttpClient(handler)), handler);
    }

    private static Task<int> Lookup(DeezerTempoClient client) =>
        client.LookupAsync("Switchfoot", "Dare You To Move", CancellationToken.None);

    [Fact]
    public async Task LookupAsync_ReturnsTheTempo()
    {
        var (client, handler) = Build();
        handler.Respond(SearchUrl, Found);
        handler.Respond("https://api.deezer.com/track/3135556", """{"id":3135556,"bpm":142.5}""");

        Assert.Equal(143, await Lookup(client));
    }

    // The search names the song and the tempo is on the track itself, so it takes two
    // calls. Worth saying out loud because it doubles the cost of a backfill.
    [Fact]
    public async Task LookupAsync_AsksTheSearchThenTheTrack()
    {
        var (client, handler) = Build();
        handler.Respond(SearchUrl, Found);
        handler.Respond("https://api.deezer.com/track/3135556", """{"bpm":142.5}""");

        await Lookup(client);

        Assert.Equal(
            [SearchUrl, "https://api.deezer.com/track/3135556"],
            handler.Requests.Select(request => request.RequestUri!.AbsoluteUri));
    }

    // A search for a song nobody has beats returning something plausible: the wrong
    // tempo is worse than none, because a workout pool built on it plays music that
    // does not keep step and there is no way to tell from the screen.
    [Fact]
    public async Task LookupAsync_RefusesAResultThatIsADifferentSong()
    {
        var (client, handler) = Build();
        handler.Respond(SearchUrl, """
            {"data":[{"id":9,"title":"Something Else Entirely","artist":{"name":"Another Band"}}]}
            """);

        Assert.Equal(0, await Lookup(client));
    }

    // The catalogue names it with the edition noise the owner's file does not carry.
    [Fact]
    public async Task LookupAsync_AcceptsTheSameSongNamedDifferently()
    {
        var (client, handler) = Build();
        handler.Respond(SearchUrl, """
            {"data":[{"id":7,"title":"Dare You To Move - 2011 Remaster","artist":{"name":"Switchfoot"}}]}
            """);
        handler.Respond("https://api.deezer.com/track/7", """{"bpm":142.5}""");

        Assert.Equal(143, await Lookup(client));
    }

    // Deezer reports 0 for a track nobody has measured, which is the same thing Score
    // means by it — unknown, not slow.
    [Fact]
    public async Task LookupAsync_ReturnsNothingForAnUnmeasuredTrack()
    {
        var (client, handler) = Build();
        handler.Respond(SearchUrl, Found);
        handler.Respond("https://api.deezer.com/track/3135556", """{"bpm":0}""");

        Assert.Equal(0, await Lookup(client));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"total":0}""")]
    public async Task LookupAsync_ReturnsNothingForAReplyItCannotUse(string body)
    {
        var (client, handler) = Build();
        handler.Respond(SearchUrl, body);

        Assert.Equal(0, await Lookup(client));
    }

    [Fact]
    public async Task LookupAsync_ReturnsNothingWhenTheServiceIsDown()
    {
        var (client, _) = Build();

        Assert.Equal(0, await Lookup(client));
    }

    [Fact]
    public async Task LookupAsync_AsksNothingWithoutAnArtistOrTitle()
    {
        var (client, handler) = Build();

        Assert.Equal(0, await client.LookupAsync("", "", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }
}
