using Codex.Domain.Music;
using Codex.Domain.Web;
using Xunit;

namespace Codex.Tests.Web;

public class SuiteRedirectTests
{
    // Outside Admin mode a sign-in opens in Flyleaf's restricted browser, whose gate refuses every
    // non-web scheme — so TIDAL's redirect back to Score landed on the "Blocked" page and the code
    // never arrived. A suite app's own redirect is the one non-web scheme it hands on.
    [Fact]
    public void ScoresTidalRedirect_IsHandedBack()
    {
        Assert.True(SuiteRedirect.IsSuiteRedirect(new Uri(TidalSignIn.RedirectUri + "?code=abc&state=xyz")));
    }

    // Chick-fil-A's sign-in ends by redirecting to its own app (cfa://…/oauth2/callback). It is
    // handed to that one package only, and only for that callback path on that host.
    [Fact]
    public void ChickFilAsCallback_IsHandedToItsApp()
    {
        var callback = new Uri("cfa://order.chick-fil-a.com/oauth2/callback?code=abc&state=xyz");
        Assert.Equal("com.chickfila.cfaflagship", SuiteRedirect.AppRedirectPackage(callback));
    }

    [Theory]
    [InlineData("cfa://evil.example.com/oauth2/callback?code=abc")]
    [InlineData("cfa://order.chick-fil-a.com/somewhere/else")]
    [InlineData("cfa://order.chick-fil-a.com/oauth2/callbackx")]
    [InlineData("cfb://order.chick-fil-a.com/oauth2/callback?code=abc")]
    [InlineData("https://order.chick-fil-a.com/oauth2/callback")]
    [InlineData("intent://order.chick-fil-a.com/oauth2/callback#Intent;scheme=cfa;end")]
    public void OtherCallbacks_AreNotHandedToAnApp(string url)
    {
        Assert.Null(SuiteRedirect.AppRedirectPackage(new Uri(url)));
    }

    [Theory]
    [InlineData("https://com.storyworthtelling.example/")]
    [InlineData("http://login.tidal.com/authorize")]
    [InlineData("intent://scan/#Intent;scheme=zxing;end")]
    [InlineData("market://details?id=com.storyworthtelling.focusphone.audio")]
    [InlineData("javascript:alert(1)")]
    [InlineData("tidal://login/auth?code=abc")]
    [InlineData("com.storyworthtellingx.evil://back")]
    [InlineData("com.other.app://back")]
    public void AnythingElse_StaysBlocked(string url)
    {
        Assert.False(SuiteRedirect.IsSuiteRedirect(new Uri(url)));
    }
}
