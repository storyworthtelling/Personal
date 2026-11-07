using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// The durable logs are uploaded by the log loop, so the values the privacy report (§4) and the logging
/// audit (08) found in them must not come back: a full phone number and an email's link in Post, a lender
/// name and a balance in Index's balance hand-off, the model's raw transcription of a handwritten page, and
/// a list row's text in either tap watch. Each case reads the source of one call site.
/// </summary>
public partial class RuntimeLogsCarryNoValuesTests
{
    [Fact]
    public void PostLogsABlockedNumberOnlyAsItsLastDigits()
    {
        var source = Read("FocusPhone/Post.Android/MailboxArea.cs");

        Assert.DoesNotMatch(LogCallWith("number"), source);
        Assert.Contains("LogSafe.Number(number)", source);
    }

    [Fact]
    public void PostLogsAnEmailLinkOnlyAsItsHost()
    {
        var source = Read("FocusPhone/Post.Android/LinkOpener.cs");

        Assert.DoesNotMatch(LogCallWith("url"), source);
        Assert.Contains("LogSafe.Link(url)", source);
    }

    [Fact]
    public void IndexLogsNoLenderNameOrBalanceFromTheHandOff()
    {
        var source = Read("Ledger/Ledger.Android/Platforms/Android/BalanceReceiver.cs");

        Assert.DoesNotMatch(LogCallWith("balance"), source);
        Assert.DoesNotMatch(LogCallWith("sender"), source);
        Assert.DoesNotMatch(new Regex(@"Log\w*\([^;]*""[^""]*\{Sender\}"), source);
    }

    [Fact]
    public void TheHandwritingReaderNeverLogsTheTranscription()
    {
        var source = Read("Storyworthtelling.Handwriting/ClaudeVisionHandwritingExtractor.cs");

        Assert.DoesNotMatch(new Regex(@"Log\w*\([^;]*\{Text\}[^;]*,\s*text\s*\)"), source);
    }

    [Theory]
    [InlineData("FocusPhone/Codex.Ui.Android/Diagnostics/TapWatch.cs")]
    [InlineData("Storyworthtelling.Maui/Diagnostics/MauiTapWatch.cs")]
    public void BothTapWatchesNameAListRowWithoutItsText(string file)
    {
        var source = Read(file);

        Assert.Contains("var inList = InList(view);", source);
        Assert.Matches(new Regex(@"TapWatchCore\.ControlName\([^;]*inList:\s*inList\)"), source);
    }

    [Fact]
    public void TheGuardSeesAnInterpolatedValue()
    {
        Assert.Matches(LogCallWith("number"), "PostDiagnostics.LogWarning(\"Block\", $\"Could not block {number}\", e);");
        Assert.DoesNotMatch(LogCallWith("number"), "PostDiagnostics.LogWarning(\"Block\", $\"Could not block {LogSafe.Number(number)}\", e);");
    }

    /// <summary>A log call whose interpolated message holds <paramref name="name"/> bare, as <c>{name}</c> or <c>{name:format}</c>.</summary>
    private static Regex LogCallWith(string name) =>
        new(@"(?:Log\w*|Warn|Info|LogUnroutable)\([^;]*\$""[^""]*\{" + Regex.Escape(name) + @"(?::[^}]*)?\}");

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));
}
