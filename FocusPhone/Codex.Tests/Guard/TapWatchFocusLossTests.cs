using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// A tap whose answer opens in another window (another app, a platform dialog, the system's picker) changes
/// nothing the layout fingerprint sees; the activity's window loses focus instead. Unless the window callback says
/// so, every such tap matures to DEAD TAP. And a long press opens its answer before lift, so the tap carries the
/// time the finger went down. The head cannot be loaded here, so this reads the kit's source.
/// </summary>
public class TapWatchFocusLossTests
{
    private const string TapWatchFile = "FocusPhone/Codex.Ui.Android/Diagnostics/TapWatch.cs";

    [Fact]
    public void FocusLostTellsTheJudgeTheWindowWasLeft()
    {
        var source = LogLoopSource.File(TapWatchFile);
        var body = Regex.Match(source, @"void OnWindowFocusChanged\(bool hasFocus\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);

        Assert.True(body.Success, "WatchedCallback.OnWindowFocusChanged should have a body.");
        Assert.Matches(new Regex(@"else\s*\{.*?core\?\.WindowLeft\(\)", RegexOptions.Singleline), body.Groups["body"].Value);
    }

    [Fact]
    public void ATapCarriesTheTimeTheFingerWentDown()
    {
        var source = LogLoopSource.File(TapWatchFile);

        Assert.Matches(new Regex(@"watch\.Touched\(name,[^;]*pressedAtMs:"), source);
    }
}
