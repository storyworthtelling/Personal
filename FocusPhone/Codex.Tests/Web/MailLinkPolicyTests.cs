using Codex.Domain.Web;
using Xunit;

namespace Codex.Tests.Web;

/// <summary>
/// Mail HTML and a web page get the SAME engine and deliberately different postures.
///
/// A browsing page is navigated: JavaScript runs, and a link within the site follows in
/// place. An email is READ: nothing in it should execute, and no link in it should ever
/// navigate the reader — every one is handed to the restricted browser, where the existing
/// origin-containment rules apply to it like any other link. Mail is the one surface where
/// the content is written by whoever felt like writing to the owner.
/// </summary>
public class MailLinkPolicyTests
{
    [Fact]
    public void ReadingMailNeverRunsScript()
    {
        Assert.False(WebPosture.Reading.AllowsScript);
    }

    // JavaScript in a browsing page is not optional — half the web will not render — but it
    // is exactly what an email must not be allowed to do.
    [Fact]
    public void BrowsingRunsScript()
    {
        Assert.True(WebPosture.Browsing.AllowsScript);
    }

    // Remote images load in mail: the owner's explicit call, accepting the tracking pixel.
    [Fact]
    public void ReadingLoadsRemoteImages()
    {
        Assert.True(WebPosture.Reading.LoadsRemoteImages);
    }

    // The load-bearing difference. A link in an email must LEAVE, so the message can never
    // navigate itself somewhere and keep the reader's trust in what they are looking at.
    [Fact]
    public void NoLinkInAnEmailNavigatesInPlace()
    {
        Assert.False(WebPosture.Reading.AllowsInPlaceNavigation);
        Assert.True(WebPosture.Browsing.AllowsInPlaceNavigation);
    }

    // Neither posture may touch the device's files. A mail body reaching file:// would be
    // reading the owner's storage on a sender's instruction.
    [Fact]
    public void NeitherPostureReachesTheFileSystem()
    {
        Assert.False(WebPosture.Reading.AllowsFileAccess);
        Assert.False(WebPosture.Browsing.AllowsFileAccess);
    }

    // Safe Browsing stays on everywhere: it costs nothing and it is the one protection that
    // works against a link nobody has seen before.
    [Fact]
    public void SafeBrowsingIsOnInBothPostures()
    {
        Assert.True(WebPosture.Reading.SafeBrowsing);
        Assert.True(WebPosture.Browsing.SafeBrowsing);
    }
}
