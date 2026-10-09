using Codex.Domain.Web;
using Xunit;

namespace Codex.Tests.Web;

public class LinkPolicyTests
{
    private static readonly LinkPolicy Policy = new(PublicSuffixList.Parse("""
        com
        co.uk
        io
        github.io
        """));

    private static Uri U(string url) => new(url);

    [Fact]
    public void SameHost_IsAllowed()
    {
        Assert.True(Policy.IsNavigationAllowed(U("https://example.com/a"), U("https://example.com/b")));
    }

    [Fact]
    public void Subdomain_OfSameRegistrableDomain_IsAllowed()
    {
        Assert.True(Policy.IsNavigationAllowed(U("https://www.example.com/"), U("https://shop.example.com/cart")));
    }

    [Fact]
    public void DifferentRegistrableDomain_IsBlocked()
    {
        Assert.False(Policy.IsNavigationAllowed(U("https://example.com/"), U("https://evil.com/")));
    }

    [Fact]
    public void SharedPublicSuffix_IsNotSharedDomain()
    {
        Assert.False(Policy.IsNavigationAllowed(
            U("https://a.example.co.uk/"), U("https://other.co.uk/")));
        Assert.False(Policy.IsNavigationAllowed(
            U("https://alice.github.io/"), U("https://bob.github.io/")));
    }

    [Fact]
    public void SchemeChange_BetweenHttpAndHttps_IsAllowed()
    {
        Assert.True(Policy.IsNavigationAllowed(U("http://example.com/"), U("https://example.com/")));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("intent://scan/#Intent;scheme=zxing;end")]
    [InlineData("market://details?id=com.example")]
    [InlineData("file:///etc/passwd")]
    public void NonHttpTargetScheme_IsBlocked(string target)
    {
        Assert.False(Policy.IsNavigationAllowed(U("https://example.com/"), U(target)));
    }

    [Fact]
    public void TargetWithoutHost_IsBlocked()
    {
        Assert.False(Policy.IsNavigationAllowed(U("https://example.com/"), U("about:blank")));
    }

    [Fact]
    public void OriginHostEqualToPublicSuffix_BlocksEverything()
    {
        Assert.False(Policy.IsNavigationAllowed(U("https://co.uk/"), U("https://co.uk/page")));
    }

    [Fact]
    public void GoogleAuthHost_IsAllowedEvenCrossDomain_SoLoginsComplete()
    {
        // A "Sign in with Google" redirect from an app's site must be allowed to cross domains.
        Assert.True(Policy.IsNavigationAllowed(
            U("https://order.example.com/login"), U("https://accounts.google.com/o/oauth2/auth")));
    }

    [Fact]
    public void SocialHost_IsBlocked_EvenFromItsOwnDomain()
    {
        // Explicit social block: not reachable even when the link's origin is that same site.
        Assert.False(Policy.IsNavigationAllowed(
            U("https://instagram.com/"), U("https://instagram.com/explore")));
        Assert.False(Policy.IsNavigationAllowed(
            U("https://news.example.com/"), U("https://www.facebook.com/somepage")));
    }

    [Theory]
    [InlineData("https://play.google.com/store/apps")]
    [InlineData("https://play.google.com/store/apps/details?id=com.example")]
    [InlineData("https://play.app.goo.gl/?link=https://play.google.com/store/apps/details?id=com.example")]
    [InlineData("https://market.android.com/details?id=com.example")]
    public void PlayStoreHost_IsBlocked_EvenFromAGoogleOrigin(string target)
    {
        // The Play Store's website is closed in the restricted browser. Google is a single registrable
        // domain, so this has to be a host-level block, not a domain one, or every Google page closes too.
        Assert.False(Policy.IsNavigationAllowed(U("https://www.google.com/search?q=apps"), U(target)));
    }

    [Fact]
    public void PlayStoreHost_IsBlocked_FromItsOwnPage()
    {
        Assert.False(Policy.IsNavigationAllowed(
            U("https://play.google.com/store/apps"), U("https://play.google.com/store/games")));
    }

    [Fact]
    public void OtherGoogleHosts_StayAllowed_OnlyPlayIsClosed()
    {
        Assert.True(Policy.IsNavigationAllowed(
            U("https://www.google.com/maps"), U("https://www.google.com/maps/place/x")));
        Assert.True(Policy.IsNavigationAllowed(
            U("https://maps.google.com/"), U("https://accounts.google.com/o/oauth2/auth")));
    }
}
