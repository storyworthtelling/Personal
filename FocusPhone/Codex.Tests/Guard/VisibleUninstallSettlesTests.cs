using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Off the device-owner path (and after the silent path throws), Flyleaf hands an uninstall to the system's own
/// uninstaller. Started as a new task it never answered, so the key action matured to ACTION NO RESULT and the
/// "Uninstalling…" notice stayed. It is now started for a result and the result settles both. The head cannot be
/// loaded here, so this reads its source.
/// </summary>
public class VisibleUninstallSettlesTests
{
    private const string Home = "FocusPhone/Flyleaf.Android/HomeActivity.cs";

    private static string Method(string source, string signature)
    {
        var match = Regex.Match(source, Regex.Escape(signature) + @"[^\n]*\n    \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(match.Success, $"{signature} not found in {Home}.");
        return match.Groups["body"].Value;
    }

    [Fact]
    public void TheVisibleUninstallerIsStartedForAResult()
    {
        var body = Method(LogLoopSource.File(Home), "private void LaunchVisibleUninstaller(string package)");

        Assert.Contains("StartActivityForResult(", body, StringComparison.Ordinal);
        Assert.Contains("Intent.ExtraReturnResult", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityFlags.NewTask", body, StringComparison.Ordinal);
        Assert.Contains("VisibleUninstall.Unavailable", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheResultSettlesTheKeyActionAndTheNotice()
    {
        var body = Method(LogLoopSource.File(Home), "protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)");

        Assert.Contains("RequestUninstallCode", body, StringComparison.Ordinal);
        Assert.Contains("VisibleUninstall.Read(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlingTakesThePendingActionAndClearsTheNotice()
    {
        var body = Method(LogLoopSource.File(Home), "private void SettleVisibleUninstall(");

        Assert.Contains("PendingUninstalls.TryRemove(", body, StringComparison.Ordinal);
        Assert.Contains(".Succeeded()", body, StringComparison.Ordinal);
        Assert.Contains(".Failed(", body, StringComparison.Ordinal);
        Assert.Contains("UninstallNoticeKey", body, StringComparison.Ordinal);
        Assert.Contains("ReloadScene()", body, StringComparison.Ordinal);
    }
}
