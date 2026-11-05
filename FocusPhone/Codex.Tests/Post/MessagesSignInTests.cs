using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Pairing Post with Google Messages takes the cookies of a messages.google.com sign-in, and
/// the sign-in happens in Post's own web view because Chrome keeps its cookies to itself. Which
/// cookies count, when the sign-in is over, and where the page may go are decided here.
/// The values are invented.
/// </summary>
public class MessagesSignInTests
{
    private const string FullHeader =
        "SID=sid-value; HSID=hsid-value; SSID=ssid-value; APISID=apisid-value; "
        + "SAPISID=sapisid-value; OSID=osid-value; __Secure-1PSIDTS=ts-value; NID=unrelated";

    [Fact]
    public void AFullSignInReadsAsComplete()
    {
        var cookies = MessagesSignIn.Read(FullHeader);

        Assert.True(cookies.IsComplete);
        Assert.Empty(cookies.Missing);
        Assert.Equal("sapisid-value", cookies.Cookies["SAPISID"]);
        Assert.Equal("ts-value", cookies.Cookies["__Secure-1PSIDTS"]);
    }

    // Only the cookies pairing uses are handed to the bridge; the rest of what Google set
    // in the web view stays there.
    [Fact]
    public void OnlyThePairingCookiesAreKept()
    {
        var cookies = MessagesSignIn.Read(FullHeader);

        Assert.DoesNotContain("NID", cookies.Cookies.Keys);
        Assert.Equal(7, cookies.Cookies.Count);
    }

    // OSID is set only by messages.google.com itself, so a sign-in that never reached
    // Messages is missing it. Missing names are reported (never values) so the log can say why.
    [Fact]
    public void ASignInThatNeverReachedMessagesIsIncompleteAndSaysWhatIsMissing()
    {
        var cookies = MessagesSignIn.Read("SID=a; HSID=b; SSID=c; APISID=d; SAPISID=e");

        Assert.False(cookies.IsComplete);
        Assert.Equal(["OSID"], cookies.Missing);
    }

    // The timestamp cookie helps but is not required (mautrix-gmessages makes the same call).
    [Fact]
    public void TheTimestampCookieIsOptional()
    {
        var cookies = MessagesSignIn.Read("SID=a; HSID=b; SSID=c; APISID=d; SAPISID=e; OSID=f");

        Assert.True(cookies.IsComplete);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(";;  ; =x; SID")]
    public void NothingUsableReadsAsNothingSignedIn(string? header)
    {
        var cookies = MessagesSignIn.Read(header);

        Assert.False(cookies.IsComplete);
        Assert.Equal(MessagesSignIn.RequiredCookies.Count, cookies.Missing.Count);
    }

    // The same name can come back twice (a host cookie and a domain cookie). The first is
    // the more specific one, and an empty value never counts as a cookie.
    [Fact]
    public void TheFirstNonEmptyValueOfANameWins()
    {
        var cookies = MessagesSignIn.Read("OSID=; OSID=first; OSID=second");

        Assert.Equal("first", cookies.Cookies["OSID"]);
    }

    [Fact]
    public void AValueMayContainAnEqualsSign()
    {
        var cookies = MessagesSignIn.Read("SAPISID=abc/def=ghi");

        Assert.Equal("abc/def=ghi", cookies.Cookies["SAPISID"]);
    }

    [Fact]
    public void TheCookiesTravelToTheBridgeAsAJsonObject()
    {
        var json = MessagesSignIn.Read("SID=a\"b; OSID=c").ToJson();

        Assert.Equal("{\"SID\":\"a\\u0022b\",\"OSID\":\"c\"}", json);
    }

    // The sign-in is over once the page lands on Messages itself; until then it is still
    // on Google's account pages.
    [Theory]
    [InlineData("https://messages.google.com/web/config", true)]
    [InlineData("https://messages.google.com/web/conversations", true)]
    [InlineData("https://accounts.google.com/v3/signin/identifier", false)]
    [InlineData("http://messages.google.com/web/config", false)]
    [InlineData("https://messages.google.com.example.com/web", false)]
    public void TheSignInEndsOnMessages(string url, bool finished)
    {
        Assert.Equal(finished, MessagesSignIn.HasReachedMessages(new Uri(url)));
    }

    // The page may go where Google's sign-in goes (its account pages, the YouTube cookie hop)
    // and nowhere else: it is a sign-in page, not a browser.
    [Theory]
    [InlineData("https://accounts.google.com/AccountChooser", true)]
    [InlineData("https://messages.google.com/web/config", true)]
    [InlineData("https://accounts.youtube.com/accounts/SetSID", true)]
    [InlineData("https://myaccount.google.com/signinoptions", true)]
    [InlineData("https://www.google.com/accounts/recovery", true)]
    [InlineData("http://accounts.google.com/AccountChooser", false)]
    [InlineData("https://www.youtube.com/watch", false)]
    [InlineData("https://evilgoogle.com/", false)]
    [InlineData("https://accounts.google.com.example.com/", false)]
    [InlineData("https://example.com/", false)]
    public void ThePageStaysOnGoogleSignIn(string url, bool allowed)
    {
        Assert.Equal(allowed, MessagesSignIn.AllowsNavigation(new Uri(url)));
    }

    [Fact]
    public void TheSignInStartsAtGooglesAccountChooserAndContinuesToMessages()
    {
        Assert.Equal("accounts.google.com", MessagesSignIn.StartUrl.Host);
        Assert.Contains("continue=https://messages.google.com/web/config", MessagesSignIn.StartUrl.OriginalString);
    }
}
