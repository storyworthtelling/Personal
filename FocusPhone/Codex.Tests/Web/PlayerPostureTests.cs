using Codex.Domain.Web;
using Xunit;

namespace Codex.Tests.Web;

/// <summary>
/// The third posture. Pulp plays exactly one thing — a YouTube trailer — and neither existing
/// posture can: Reading turns script off (the IFrame player is script), Browsing follows links in
/// place (a player that navigates is a browser). Player is script on, nothing navigates, nothing
/// plays until the owner taps play, and it loads only a YouTube embed of a validated video id.
/// </summary>
public class PlayerPostureTests
{
    [Fact]
    public void PlayerRunsScript()
    {
        Assert.True(WebPosture.Player.AllowsScript);
    }

    // A trailer never autoplays (D77): the owner taps play inside the embed.
    [Fact]
    public void NoPostureEverPlaysMediaWithoutAGesture()
    {
        Assert.False(WebPosture.Player.PlaysMediaWithoutGesture);
        Assert.False(WebPosture.Reading.PlaysMediaWithoutGesture);
        Assert.False(WebPosture.Browsing.PlaysMediaWithoutGesture);
    }

    // The load-bearing difference from Reading: a refused link in mail is handed to the restricted
    // browser; a link in the player (the YouTube logo, "watch on YouTube") goes nowhere at all.
    [Fact]
    public void ThePlayerRefusesEveryNavigationAndHandsNothingOut()
    {
        Assert.False(WebPosture.Player.AllowsInPlaceNavigation);
        Assert.Equal(NavigationVerdict.Refuse, WebPosture.Player.OnNavigation());
    }

    // The two existing postures keep exactly what they did.
    [Fact]
    public void ReadingStillHandsLinksOutAndBrowsingStillFollowsThem()
    {
        Assert.Equal(NavigationVerdict.HandOut, WebPosture.Reading.OnNavigation());
        Assert.Equal(NavigationVerdict.Follow, WebPosture.Browsing.OnNavigation());
    }

    [Fact]
    public void OnlyThePlayerGoesFullscreen()
    {
        Assert.True(WebPosture.Player.AllowsFullscreen);
        Assert.False(WebPosture.Reading.AllowsFullscreen);
        Assert.False(WebPosture.Browsing.AllowsFullscreen);
    }

    [Fact]
    public void ThePlayerIsHardenedLikeEveryOtherPosture()
    {
        Assert.False(WebPosture.Player.AllowsFileAccess);
        Assert.True(WebPosture.Player.SafeBrowsing);
    }

    [Theory]
    [InlineData("aBcDeFgHiJ0")]
    [InlineData("a-b_c-d_e-f")]
    [InlineData("00000000000")]
    public void ElevenUrlSafeCharactersAreAVideoId(string id)
    {
        Assert.True(PlayerEmbed.IsVideoId(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("aBcDeFgHiJ")]          // ten
    [InlineData("aBcDeFgHiJ0K")]        // twelve
    [InlineData("aBcDeFgHi/0")]         // a path separator
    [InlineData("aBcDeFgHi?0")]         // a query
    [InlineData("aBcDeFg%2F0")]         // an escape
    [InlineData("aBcDeFgHi 0")]         // a space
    [InlineData("aBcDeFgHi+0")]         // standard base64, not the URL-safe alphabet
    [InlineData("aBcDeFgHié0")]    // a non-ASCII letter
    [InlineData("aBcDeFgHiJ0\n")]       // a trailing newline: a "^...$" regex lets this through
    public void AnythingElseIsNotAVideoId(string? id)
    {
        Assert.False(PlayerEmbed.IsVideoId(id));
    }

    [Fact]
    public void AnEmbedUrlIsTheNoCookieEmbedOfTheIdThatNeverAutoplays()
    {
        var url = PlayerEmbed.EmbedUrl("aBcDeFgHiJ0");

        Assert.Equal("https", url.Scheme);
        Assert.Equal("www.youtube-nocookie.com", url.Host);
        Assert.Equal("/embed/aBcDeFgHiJ0", url.AbsolutePath);
        Assert.Contains("autoplay=0", url.Query, StringComparison.Ordinal);
        Assert.True(PlayerEmbed.IsEmbedUrl(url));
    }

    [Theory]
    [InlineData("aBcDeFgHi/0")]
    [InlineData("../../watch")]
    [InlineData("")]
    public void AnInvalidIdNeverBecomesAUrl(string id)
    {
        Assert.Throws<ArgumentException>(() => PlayerEmbed.EmbedUrl(id));
    }

    [Theory]
    [InlineData("https://www.youtube-nocookie.com/embed/aBcDeFgHiJ0")]
    [InlineData("https://www.youtube.com/embed/aBcDeFgHiJ0?autoplay=0")]
    [InlineData("https://youtube.com/embed/aBcDeFgHiJ0")]
    public void YouTubeEmbedsAreEmbeds(string url)
    {
        Assert.True(PlayerEmbed.IsEmbedUrl(new Uri(url)));
    }

    [Theory]
    [InlineData("http://www.youtube-nocookie.com/embed/aBcDeFgHiJ0")]   // not https
    [InlineData("https://www.youtube.com/watch?v=aBcDeFgHiJ0")]        // the site, not the embed
    [InlineData("https://www.youtube.com/embed/aBcDeFgHiJ0/extra")]
    [InlineData("https://www.youtube.com/embed/not-an-id")]
    [InlineData("https://www.youtube.com.example.com/embed/aBcDeFgHiJ0")]
    [InlineData("https://player.example.com/embed/aBcDeFgHiJ0")]
    [InlineData("https://user@www.youtube.com/embed/aBcDeFgHiJ0")]
    [InlineData("https://www.youtube.com:8443/embed/aBcDeFgHiJ0")]
    [InlineData("file:///embed/aBcDeFgHiJ0")]
    public void NothingElseIsAnEmbed(string url)
    {
        Assert.False(PlayerEmbed.IsEmbedUrl(new Uri(url)));
    }

    // YouTube refuses an embed with no Referer (error 153); the page is loaded at one fixed https
    // origin naming the app, so the iframe's request always carries one.
    [Fact]
    public void TheBaseUrlIsAFixedHttpsOriginNamingTheApp()
    {
        Assert.Equal("https://com.example.player/", PlayerEmbed.BaseUrlFor("com.example.player"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("com.example/evil")]
    [InlineData("com.example.player@other.example.com")]
    [InlineData("com..example")]
    [InlineData("Com.Example")]
    public void ABaseUrlIsOnlyMadeFromAPackageName(string applicationId)
    {
        Assert.Throws<ArgumentException>(() => PlayerEmbed.BaseUrlFor(applicationId));
    }
    // Since September 2018 YouTube no longer lets an embed turn related videos off; rel=0 limits
    // them to the same channel. Annotations off; the fullscreen button stated rather than assumed;
    // the IFrame API enabled so the page can re-cue the trailer when it ends (see PaperWebView).
    // https://developers.google.com/youtube/player_parameters
    [Fact]
    public void AnEmbedUrlLimitsWhatTheEndOffersAndCanBeReCued()
    {
        var query = PlayerEmbed.EmbedUrl("aBcDeFgHiJ0").Query;

        Assert.Contains("rel=0", query, StringComparison.Ordinal);
        Assert.Contains("iv_load_policy=3", query, StringComparison.Ordinal);
        Assert.Contains("fs=1", query, StringComparison.Ordinal);
        Assert.Contains("enablejsapi=1", query, StringComparison.Ordinal);
        Assert.Contains("playsinline=1", query, StringComparison.Ordinal);
    }

    // With the IFrame API on, YouTube asks for the host page's origin as an extra check; it is the
    // same fixed origin the page is loaded at, with no trailing slash.
    [Fact]
    public void AnEmbedUrlForAnAppNamesThePagesOrigin()
    {
        var url = PlayerEmbed.EmbedUrl("aBcDeFgHiJ0", "com.example.player");

        Assert.Contains("origin=https%3A%2F%2Fcom.example.player", url.Query, StringComparison.Ordinal);
        Assert.True(PlayerEmbed.IsEmbedUrl(url));
        Assert.Throws<ArgumentException>(() => PlayerEmbed.EmbedUrl("aBcDeFgHiJ0", "com.example/evil"));
    }

    // Android documents that shouldOverrideUrlLoading "may be called for subframes". The player's
    // client refuses everything except the one iframe load the page itself asked for: the exact
    // embed (same host, same video), in a subframe. Anything in the main frame, or any other
    // address, is refused.
    [Fact]
    public void OnlyTheEmbedItselfMayLoadAndOnlyInASubframe()
    {
        var embed = PlayerEmbed.EmbedUrl("aBcDeFgHiJ0", "com.example.player");

        Assert.True(PlayerEmbed.AllowsFrameLoad(embed, forMainFrame: false, embed));
        Assert.False(PlayerEmbed.AllowsFrameLoad(embed, forMainFrame: true, embed));
        Assert.False(PlayerEmbed.AllowsFrameLoad(PlayerEmbed.EmbedUrl("zYxWvUtSrQ9", "com.example.player"), forMainFrame: false, embed));
        // The same trailer with its query spelled differently is still the trailer.
        Assert.True(PlayerEmbed.AllowsFrameLoad(PlayerEmbed.EmbedUrl("aBcDeFgHiJ0"), forMainFrame: false, embed));
        Assert.False(PlayerEmbed.AllowsFrameLoad(new Uri("https://www.youtube.com/embed/aBcDeFgHiJ0"), forMainFrame: false, embed));
        Assert.False(PlayerEmbed.AllowsFrameLoad(new Uri("https://www.youtube.com/watch?v=aBcDeFgHiJ0"), forMainFrame: false, embed));
        Assert.False(PlayerEmbed.AllowsFrameLoad(null, forMainFrame: false, embed));
    }
}
