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
}
