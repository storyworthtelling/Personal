using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class SharedPlaylistLinkTests
{
    private const string Id = "37i9dQZF1DXcBWIGoYBM5M";

    // What the share sheet actually hands over: a sentence, the link, and a footer.
    [Fact]
    public void Parse_FindsTheIdInSharedText()
    {
        var shared = $"Sunday setlist\nhttps://open.spotify.com/playlist/{Id}?si=8f2c1\n\nShared via Spotify";

        Assert.Equal(Id, SharedPlaylistLink.Parse(shared));
    }

    [Fact]
    public void Parse_ReadsABareUrl()
    {
        Assert.Equal(Id, SharedPlaylistLink.Parse($"https://open.spotify.com/playlist/{Id}"));
    }

    // The app-to-app form, which carries no scheme at all.
    [Fact]
    public void Parse_ReadsTheUriScheme()
    {
        Assert.Equal(Id, SharedPlaylistLink.Parse($"spotify:playlist:{Id}"));
    }

    // A localized client puts its market in the path.
    [Fact]
    public void Parse_ReadsALocalizedUrl()
    {
        Assert.Equal(Id, SharedPlaylistLink.Parse($"https://open.spotify.com/intl-pt/playlist/{Id}"));
    }

    [Fact]
    public void Parse_IgnoresTrailingPunctuation()
    {
        Assert.Equal(Id, SharedPlaylistLink.Parse($"here: https://open.spotify.com/playlist/{Id}."));
    }

    // Sharing an album or a song is a different intent, and guessing at it would
    // import the wrong thing rather than say it could not.
    [Theory]
    [InlineData("https://open.spotify.com/album/37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("https://open.spotify.com/track/37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("spotify:album:37i9dQZF1DXcBWIGoYBM5M")]
    public void Parse_RejectsSomethingThatIsNotAPlaylist(string shared)
    {
        Assert.Null(SharedPlaylistLink.Parse(shared));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("just some text the choir sent")]
    [InlineData("https://music.example.com/playlist/37i9dQZF1DXcBWIGoYBM5M")]
    public void Parse_ReturnsNothingWhenThereIsNoLink(string? shared)
    {
        Assert.Null(SharedPlaylistLink.Parse(shared));
    }

    [Fact]
    public void Parse_TakesTheFirstLinkWhenSeveralAreShared()
    {
        var shared = $"https://open.spotify.com/playlist/{Id} and https://open.spotify.com/playlist/1AbCdEfGhIjKlMnOpQrStU";

        Assert.Equal(Id, SharedPlaylistLink.Parse(shared));
    }
}
