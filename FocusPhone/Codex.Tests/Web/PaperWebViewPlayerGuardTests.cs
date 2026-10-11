using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Web;

/// <summary>
/// The Android half of the postures. <c>WebPosture</c> decides in Domain; <c>PaperWebView</c> (an
/// Android library this suite cannot load) is what turns each verdict into a client and a setting.
/// A swapped case there — mail given the client that refuses and hands nothing out, the player given
/// the one that hands every link to the browser — passes every Domain test, so this reads the file.
/// Each check first asserts it found what it checks, so a renamed method cannot pass by vanishing.
/// </summary>
public class PaperWebViewPlayerGuardTests
{
    private static string Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var file = Path.Combine(directory!.FullName, "FocusPhone", "Codex.Ui.Android", "PaperWebView.cs");
        Assert.True(File.Exists(file), "PaperWebView.cs was not found at " + file);
        return File.ReadAllText(file);
    }

    /// <summary>The text of a member from its signature to the next member at the same level.</summary>
    private static string Member(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find '{signature}' in PaperWebView.cs.");
        var next = Regex.Match(source[(start + signature.Length)..], @"\n    (public|private|internal) ");
        Assert.True(next.Success, $"Could not find the end of '{signature}'.");
        return source.Substring(start, signature.Length + next.Index);
    }

    /// <summary>The statements under one <c>case</c> label, up to the next label or the switch's end.</summary>
    private static string Case(string body, string label)
    {
        var match = Regex.Match(body, Regex.Escape(label) + @"(?<arm>.*?)(case |default:|\n        \})", RegexOptions.Singleline);
        Assert.True(match.Success, $"Harden has no '{label}' arm.");
        return match.Groups["arm"].Value;
    }

    [Fact]
    public void HardenGivesEachVerdictItsOwnClient()
    {
        var harden = Member(Source(), "public static void Harden(");

        var handOut = Case(harden, "case NavigationVerdict.HandOut:");
        Assert.Contains("new LinksAlwaysLeaveClient(", handOut, StringComparison.Ordinal);
        Assert.DoesNotContain("NothingNavigatesClient", handOut, StringComparison.Ordinal);

        var refuse = Case(harden, "case NavigationVerdict.Refuse:");
        Assert.Contains("new NothingNavigatesClient(", refuse, StringComparison.Ordinal);
        Assert.DoesNotContain("LinksAlwaysLeaveClient", refuse, StringComparison.Ordinal);

        var follow = Case(harden, "case NavigationVerdict.Follow:");
        Assert.DoesNotContain("SetWebViewClient", follow, StringComparison.Ordinal);
    }

    [Fact]
    public void HardenTakesTheGestureRuleFromThePosture()
    {
        var harden = Member(Source(), "public static void Harden(");

        Assert.Matches(@"settings\.MediaPlaybackRequiresUserGesture\s*=\s*!posture\.PlaysMediaWithoutGesture\s*;", harden);
    }

    // N4: IsEmbedUrl is the documented guard on what the player may hold, so it is checked on the
    // URL actually loaded, and the load is refused rather than made when it fails.
    [Fact]
    public void LoadPlayerChecksTheUrlItLoadsIsAnEmbed()
    {
        var player = Member(Source(), "public static PlayerChrome LoadPlayer(");

        var check = player.IndexOf("PlayerEmbed.IsEmbedUrl(embed)", StringComparison.Ordinal);
        var load = player.IndexOf("LoadDataWithBaseURL(", StringComparison.Ordinal);
        Assert.True(check >= 0, "LoadPlayer never calls PlayerEmbed.IsEmbedUrl on the embed it loads.");
        Assert.True(load >= 0, "LoadPlayer no longer loads its page with LoadDataWithBaseURL.");
        Assert.True(check < load, "LoadPlayer checks the embed only after loading it.");
        Assert.Contains("throw new", player[check..load], StringComparison.Ordinal);
    }

    // S8: when the trailer ends, YouTube's end screen offers suggestions that play inside the iframe
    // by script — no navigation for the client to refuse. The page re-cues the same video instead.
    [Fact]
    public void TheEndOfTheTrailerReCuesTheSameVideo()
    {
        var player = Member(Source(), "public static PlayerChrome LoadPlayer(");

        Assert.Contains("YT.PlayerState.ENDED", player, StringComparison.Ordinal);
        Assert.Contains("cueVideoById(", player, StringComparison.Ordinal);
        Assert.Contains("https://www.youtube.com/iframe_api", player, StringComparison.Ordinal);
        Assert.DoesNotContain("AddJavascriptInterface", Source(), StringComparison.Ordinal);
    }

    // Android: shouldOverrideUrlLoading "may be called for subframes". The player's client lets
    // through only the embed's own subframe load, decided in Domain.
    [Fact]
    public void ThePlayersClientAllowsOnlyTheEmbedSubframe()
    {
        var client = Member(Source(), "private sealed class NothingNavigatesClient");

        Assert.Contains("PlayerEmbed.AllowsFrameLoad(", client, StringComparison.Ordinal);
        Assert.Contains("IsForMainFrame", client, StringComparison.Ordinal);
    }
}
