using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Taps inside Flyleaf's restricted browser were judged dead: the tap watch sees a change through the layout and
/// the status line, a web page's content is neither, and the browser's address line moved only once a page had
/// finished loading — long after the dead window. The browser now says a navigation began when it begins, and
/// the watch never judges a tap on web content dead (it still counts it for repeats). Head source, read by content.
/// </summary>
public class WebTapsSeenTests
{
    [Fact]
    public void TheBrowserShowsTheAddressWhenAPageStarts()
    {
        var source = LogLoopSource.File("FocusPhone/Flyleaf.Android/RestrictedBrowserActivity.cs");

        var started = Regex.Match(source, @"public override void OnPageStarted\(WebView\? view, string\? url, Android\.Graphics\.Bitmap\? favicon\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);
        Assert.True(started.Success, "The browser's web view client should override OnPageStarted.");
        Assert.Contains("activity.ShowAddress(url)", started.Groups["body"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowingAnAddressTellsTheWatch()
    {
        var source = LogLoopSource.File("FocusPhone/Flyleaf.Android/RestrictedBrowserActivity.cs");

        var show = Regex.Match(source, @"public void ShowAddress\(string url\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(show.Success, "ShowAddress should have a body that tells the tap watch.");
        Assert.Contains("TapWatch.Status()", show.Groups["body"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWatchHandsAWebTapOverAsUnseen()
    {
        var watch = LogLoopSource.File("FocusPhone/Codex.Ui.Android/Diagnostics/TapWatch.cs");

        Assert.Contains("watch.TouchedUnseen(", watch, StringComparison.Ordinal);
        Assert.Contains("is Android.Webkit.WebView", watch, StringComparison.Ordinal);
    }
}
