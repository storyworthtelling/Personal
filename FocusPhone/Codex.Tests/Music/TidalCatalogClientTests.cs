using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

// Fixture shapes here are copied from a real, live GET against openapi.tidal.com/v2
// (search for "Coldplay", 28 Aug 2026) — not reconstructed from documentation, which
// is what left the Spotify client's title extraction wrong for months. Two real
// details worth calling out because they are easy to get wrong by guessing: search is
// a two-step JSON:API flow (filter by query for an opaque searchResults id, then fetch
// that resource with include= for the actual data), and a track can credit more than
// one artist.
public class TidalCatalogClientTests
{
    private const string SearchUrl =
        "https://openapi.tidal.com/v2/searchResults?filter%5Bquery%5D=Coldplay&countryCode=US";
    private const string ResultUrl =
        "https://openapi.tidal.com/v2/searchResults/58iwKJuSreaOr5FVXnJ3Vsc9skq9G7FNST" +
        "?countryCode=US&include=tracks%2Ctracks.artists%2Ctracks.albums";

    private static (TidalCatalogClient Client, FakeHttpMessageHandler Handler) Build()
    {
        var handler = new FakeHttpMessageHandler();
        return (new TidalCatalogClient(new HttpClient(handler)), handler);
    }

    private static void RespondWithSearchId(FakeHttpMessageHandler handler) =>
        handler.Respond(SearchUrl, """
            {"data":[{"id":"58iwKJuSreaOr5FVXnJ3Vsc9skq9G7FNST","type":"searchResults",
              "attributes":{"query":"Coldplay","trackingId":"f09b5098"}}]}
            """);

    [Fact]
    public async Task SearchAsync_ReadsTracksInTheRelationshipOrderWithArtistAndAlbum()
    {
        var (client, handler) = Build();
        RespondWithSearchId(handler);
        handler.Respond(ResultUrl, """
            {"data":{"id":"58iwKJuSreaOr5FVXnJ3Vsc9skq9G7FNST","type":"searchResults",
              "relationships":{"tracks":{"data":[{"id":"120268","type":"tracks"},{"id":"120272","type":"tracks"}]}}},
             "included":[
               {"id":"120267","type":"albums","attributes":{"title":"Parachutes"}},
               {"id":"8812","type":"artists","attributes":{"name":"Coldplay"}},
               {"id":"120268","type":"tracks","attributes":{"title":"Don't Panic","duration":"PT2M17S"},
                "relationships":{"albums":{"data":[{"id":"120267","type":"albums"}]},
                                  "artists":{"data":[{"id":"8812","type":"artists"}]}}},
               {"id":"120272","type":"tracks","attributes":{"title":"Yellow","duration":"PT4M29S"},
                "relationships":{"albums":{"data":[{"id":"120267","type":"albums"}]},
                                  "artists":{"data":[{"id":"8812","type":"artists"}]}}}
             ]}
            """);

        var tracks = await client.SearchAsync("token", "Coldplay", "US", CancellationToken.None);

        Assert.Equal(["Don't Panic", "Yellow"], tracks.Select(t => t.Title));
        var first = tracks[0];
        Assert.Equal("tidal:120268", first.Id);
        Assert.Equal("Coldplay", first.ArtistName);
        Assert.Equal("Parachutes", first.AlbumName);
        Assert.Equal(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(17), first.Duration);
        Assert.Equal(TrackAvailability.Streaming, first.Availability);
    }

    [Fact]
    public async Task SearchAsync_SendsTheBearerTokenAndCountryCode()
    {
        var (client, handler) = Build();
        RespondWithSearchId(handler);
        handler.Respond(ResultUrl, """{"data":{"relationships":{"tracks":{"data":[]}}},"included":[]}""");

        await client.SearchAsync("the-token", "Coldplay", "US", CancellationToken.None);

        var first = handler.Requests[0];
        Assert.Equal("Bearer", first.Headers.Authorization!.Scheme);
        Assert.Equal("the-token", first.Headers.Authorization.Parameter);
        Assert.Equal("application/vnd.api+json", first.Headers.Accept.Single().MediaType);
    }

    // Credited to two people the way a feature or a duet is — the resolver (TrackIdentity)
    // matches on any one of them, but the display name has to carry all of them.
    [Fact]
    public async Task SearchAsync_JoinsEveryCreditedArtist()
    {
        var (client, handler) = Build();
        RespondWithSearchId(handler);
        handler.Respond(ResultUrl, """
            {"data":{"relationships":{"tracks":{"data":[{"id":"1","type":"tracks"}]}}},
             "included":[
               {"id":"1","type":"tracks","attributes":{"title":"Duet","duration":"PT3M"},
                "relationships":{"artists":{"data":[{"id":"a","type":"artists"},{"id":"b","type":"artists"}]}}},
               {"id":"a","type":"artists","attributes":{"name":"First"}},
               {"id":"b","type":"artists","attributes":{"name":"Second"}}
             ]}
            """);

        var tracks = await client.SearchAsync("token", "Coldplay", "US", CancellationToken.None);

        Assert.Equal("First, Second", tracks.Single().ArtistName);
    }

    // A track the relationship names but that dropped out of "included" — TIDAL's own
    // pagination cuts included off before every referenced id is resolved. Skipped, not
    // a blank track and not a crash.
    [Fact]
    public async Task SearchAsync_SkipsATrackTheResponseReferencesButDidNotInclude()
    {
        var (client, handler) = Build();
        RespondWithSearchId(handler);
        handler.Respond(ResultUrl, """
            {"data":{"relationships":{"tracks":{"data":[{"id":"1","type":"tracks"},{"id":"missing","type":"tracks"}]}}},
             "included":[{"id":"1","type":"tracks","attributes":{"title":"Here","duration":"PT1M"}}]}
            """);

        var tracks = await client.SearchAsync("token", "Coldplay", "US", CancellationToken.None);

        Assert.Equal(["Here"], tracks.Select(t => t.Title));
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyWhenTheQueryMatchesNothing()
    {
        var (client, handler) = Build();
        handler.Respond(SearchUrl, """{"data":[]}""");

        var tracks = await client.SearchAsync("token", "Coldplay", "US", CancellationToken.None);

        Assert.Empty(tracks);
        Assert.Single(handler.Requests); // never made the second call with nothing to resolve
    }

    [Fact]
    public async Task SearchAsync_BlankQueryNeverCallsTheNetwork()
    {
        var (client, handler) = Build();

        var tracks = await client.SearchAsync("token", "   ", "US", CancellationToken.None);

        Assert.Empty(tracks);
        Assert.Empty(handler.Requests);
    }

    // Every failure is empty, the same rule the other three keyless clients follow — a
    // search field someone is typing in must never surface an exception.
    [Fact]
    public async Task SearchAsync_ReturnsEmptyWhenTheServiceErrors()
    {
        var (client, handler) = Build();
        handler.Respond(SearchUrl, """{"errors":[{"code":"UNAUTHORIZED"}]}""",
            status: System.Net.HttpStatusCode.Unauthorized);

        var tracks = await client.SearchAsync("bad-token", "Coldplay", "US", CancellationToken.None);

        Assert.Empty(tracks);
    }
}
