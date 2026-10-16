using System.Text.RegularExpressions;
using Pulp.Tests.Guard;

namespace Pulp.Tests.Trailer;

/// <summary>
/// The trailer screen, read by content (the suite runs without the Android workload). The player's
/// own rules — script on, nothing navigates, never autoplays, only a validated YouTube id — are
/// tested where they live, in <c>Codex.Tests/Web/PlayerPostureTests</c>; what is checked here is that
/// Pulp's one host keeps to them.
/// </summary>
public class TrailerActivityTests
{
    private static string Source()
    {
        var path = Path.Combine(PulpSource.Root(), PlaysNothingTests.TrailerHost);
        Assert.True(File.Exists(path), $"{PlaysNothingTests.TrailerHost} does not exist.");
        return File.ReadAllText(path);
    }

    // An exported trailer screen would let any app on the phone open Pulp's player on any video.
    [Fact]
    public void TheTrailerScreenIsNotExported()
    {
        var source = Source();
        Assert.Matches(new Regex(@"\[Activity\([^\]]*Exported\s*=\s*false", RegexOptions.Singleline), source);
        Assert.DoesNotMatch(new Regex(@"Exported\s*=\s*true"), source);
        Assert.DoesNotContain("IntentFilter", source, StringComparison.Ordinal);
    }

    // The id from the intent is input: it is checked before anything else is built from it.
    [Fact]
    public void TheVideoIdIsValidatedBeforeThePlayerLoads()
    {
        var source = Source();
        var check = source.IndexOf("PlayerEmbed.IsVideoId", StringComparison.Ordinal);
        var load = source.IndexOf("PaperWebView.LoadPlayer", StringComparison.Ordinal);

        Assert.True(check >= 0, "TrailerActivity never validates the video id.");
        Assert.True(load >= 0, "TrailerActivity never loads the player.");
        Assert.True(check < load, "TrailerActivity loads the player before validating the video id.");
    }

    // The posture is the one place the configuration is decided; the host may not loosen it.
    [Fact]
    public void TheHostDoesNotLoosenThePosture()
    {
        var source = Source();
        Assert.DoesNotMatch(new Regex(
            @"MediaPlaybackRequiresUserGesture|AddJavascriptInterface|JavaScriptEnabled|SetWebViewClient|AllowFileAccess|autoplay=1"),
            source);
    }

    // D315: the one player serves trailers and highlights; the kind changes the words only.
    [Fact]
    public void TheOnePlayerServesBothKindsWithTheirOwnWords()
    {
        var source = Source();

        Assert.Contains("ForHighlights(", source, StringComparison.Ordinal);
        Assert.Contains("PlayerCopy.Parse(", source, StringComparison.Ordinal);
        Assert.Contains("PlayerCopy.Status(kind)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Tap play to start the trailer", source, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Matches(source, @"PaperWebView\.LoadPlayer").Count);
    }
}
