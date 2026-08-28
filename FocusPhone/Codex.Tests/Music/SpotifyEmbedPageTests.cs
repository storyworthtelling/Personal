using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class SpotifyEmbedPageTests
{
    private static string Page(string json, string? title = "Sunday Setlist") =>
        "<!DOCTYPE html><html><head>" +
        (title is null ? string.Empty : $"<meta property=\"og:title\" content=\"{title}\"/>") +
        "</head><body><div id=\"__next\"></div>" +
        $"<script id=\"__NEXT_DATA__\" type=\"application/json\">{json}</script>" +
        "</body></html>";

    // The shape the embed page carries today: a track list of title + subtitle,
    // where the subtitle is the credited artist.
    [Fact]
    public void Read_TakesTheTitleAndTracksFromTheEmbeddedJson()
    {
        var html = Page("""
            {"props":{"pageProps":{"state":{"data":{"entity":{"trackList":[
              {"title":"Build My Life","subtitle":"Pat Barrett"},
              {"title":"Great Are You Lord","subtitle":"All Sons & Daughters"}]}}}}}}
            """);

        var playlist = SpotifyEmbedPage.Read(html);

        Assert.Equal("Sunday Setlist", playlist.Title);
        Assert.Equal(
            [new ImportedTrack("Build My Life", "Pat Barrett"),
             new ImportedTrack("Great Are You Lord", "All Sons & Daughters")],
            playlist.Tracks);
    }

    // The other shape the same data appears in. Reading both is the point of walking
    // the JSON rather than following one path: the path is Spotify's to change, and
    // it is the only part of this that is not ours.
    [Fact]
    public void Read_ReadsAnArtistsArrayAsWellAsASubtitle()
    {
        var html = Page("""
            {"tracks":{"items":[
              {"track":{"name":"Dare You To Move","artists":[{"name":"Switchfoot"}]}},
              {"track":{"name":"Build My Life","artists":[{"name":"Pat Barrett"},{"name":"Chris Tomlin"}]}}]}}
            """);

        var playlist = SpotifyEmbedPage.Read(html);

        Assert.Equal(
            [new ImportedTrack("Dare You To Move", "Switchfoot"),
             new ImportedTrack("Build My Life", "Pat Barrett, Chris Tomlin")],
            playlist.Tracks);
    }

    [Fact]
    public void Read_KeepsTheListedOrder()
    {
        var html = Page("""
            {"trackList":[{"title":"Third","subtitle":"C"},{"title":"First","subtitle":"A"}]}
            """);

        Assert.Equal(["Third", "First"], SpotifyEmbedPage.Read(html).Tracks.Select(track => track.Title));
    }

    // The page carries more than the playlist — related shelves, recommendations.
    // The playlist is the long list.
    [Fact]
    public void Read_TakesTheLongestTrackListOnThePage()
    {
        var html = Page("""
            {"recommended":[{"title":"Not This","subtitle":"X"}],
             "trackList":[{"title":"One","subtitle":"A"},{"title":"Two","subtitle":"B"}]}
            """);

        Assert.Equal(["One", "Two"], SpotifyEmbedPage.Read(html).Tracks.Select(track => track.Title));
    }

    // A name with nobody credited to it is not a track — it is a genre, a tag, a
    // section header. Taking those would fabricate songs the setlist never named.
    [Fact]
    public void Read_IgnoresListsThatAreNotTracks()
    {
        var html = Page("""
            {"genres":[{"name":"Worship"},{"name":"Gospel"},{"name":"Folk"}],
             "trackList":[{"title":"One","subtitle":"A"}]}
            """);

        Assert.Equal(["One"], SpotifyEmbedPage.Read(html).Tracks.Select(track => track.Title));
    }

    [Fact]
    public void Read_DecodesAnEscapedTitle()
    {
        var html = Page("""{"trackList":[{"title":"One","subtitle":"A"}]}""", "Ben &amp; Jerry&#39;s mix");

        Assert.Equal("Ben & Jerry's mix", SpotifyEmbedPage.Read(html).Title);
    }

    [Fact]
    public void Read_FallsBackToThePageTitleWhenThereIsNoOpenGraphOne()
    {
        var html = "<html><head><title>Practice list</title></head><body>" +
            "<script type=\"application/json\">{\"trackList\":[{\"title\":\"One\",\"subtitle\":\"A\"}]}</script>" +
            "</body></html>";

        Assert.Equal("Practice list", SpotifyEmbedPage.Read(html).Title);
    }

    // The failure modes that matter, because this reads a page nobody promised us:
    // it degrades to empty and the caller says it could not read the playlist.
    [Theory]
    [InlineData("<html><body>Nothing here.</body></html>")]
    [InlineData("")]
    public void Read_ReturnsNothingWhenThereIsNoEmbeddedJson(string html)
    {
        Assert.True(SpotifyEmbedPage.Read(html).IsEmpty);
    }

    [Fact]
    public void Read_ReturnsNothingWhenTheJsonIsGarbled()
    {
        var html = Page("{\"trackList\":[{\"title\":");

        Assert.True(SpotifyEmbedPage.Read(html).IsEmpty);
    }

    [Fact]
    public void Read_ReturnsNothingWhenTheJsonHoldsNoTracks()
    {
        Assert.True(SpotifyEmbedPage.Read(Page("""{"props":{"pageProps":{}}}""")).IsEmpty);
    }
}
