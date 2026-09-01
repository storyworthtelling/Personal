using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class SpotifyPlaylistClientTests
{
    private const string Id = "37i9dQZF1DXcBWIGoYBM5M";
    private const string Url = $"https://open.spotify.com/embed/playlist/{Id}";

    private const string Body = """
        <html><head><meta property="og:title" content="Sunday Setlist"/></head><body>
        <script id="__NEXT_DATA__" type="application/json">
        {"props":{"pageProps":{"state":{"data":{"entity":{"trackList":[
          {"title":"Build My Life","subtitle":"Pat Barrett"}]}}}}}}
        </script></body></html>
        """;

    private static (SpotifyPlaylistClient Client, FakeHttpMessageHandler Handler) Build()
    {
        var handler = new FakeHttpMessageHandler();
        return (new SpotifyPlaylistClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task FetchAsync_ReadsTheEmbedPageForThatPlaylist()
    {
        var (client, handler) = Build();
        handler.Respond(Url, Body, "text/html");

        var playlist = await client.FetchAsync(Id, CancellationToken.None);

        Assert.Equal(Url, handler.Requests.Single().RequestUri!.AbsoluteUri);
        Assert.Equal("Sunday Setlist", playlist.Title);
        Assert.Equal("Build My Life", playlist.Tracks.Single().Title);
    }

    // The embed page refuses a request with no browser-shaped User-Agent, so sending
    // one is not optional dressing — without it there is nothing to parse.
    [Fact]
    public async Task FetchAsync_AsksAsABrowserWould()
    {
        var (client, handler) = Build();
        handler.Respond(Url, Body, "text/html");

        await client.FetchAsync(Id, CancellationToken.None);

        Assert.NotEmpty(handler.Requests.Single().Headers.UserAgent);
    }

    // This reads a page nobody promised us. A playlist that is private, deleted, or
    // simply served differently one morning must come back empty, not throw into a
    // share the owner is watching.
    [Fact]
    public async Task FetchAsync_ReturnsNothingWhenThePageIsNotThere()
    {
        var (client, _) = Build();

        Assert.True((await client.FetchAsync(Id, CancellationToken.None)).IsEmpty);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNothingForAPageItCannotRead()
    {
        var (client, handler) = Build();
        handler.Respond(Url, "<html><body>Sign in to continue.</body></html>", "text/html");

        Assert.True((await client.FetchAsync(Id, CancellationToken.None)).IsEmpty);
    }
}
