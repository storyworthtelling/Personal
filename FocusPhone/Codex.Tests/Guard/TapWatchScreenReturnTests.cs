using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// The native tap watch learns which screen it is on when an activity attaches, in OnCreate. An activity that is
/// resumed rather than recreated (back to the home shelf from the browser it opened) never runs that again, so
/// unless the window callback says so on regaining focus, every tap there is judged on the screen created last.
/// The head cannot be loaded here, so this reads the kit's source.
/// </summary>
public class TapWatchScreenReturnTests
{
    private const string TapWatchFile = "FocusPhone/Codex.Ui.Android/Diagnostics/TapWatch.cs";

    [Fact]
    public void FocusGainedReannouncesTheCallbacksScreen()
    {
        var source = LogLoopSource.File(TapWatchFile);

        Assert.DoesNotMatch(new Regex(@"void OnWindowFocusChanged\(bool hasFocus\)\s*=>\s*inner\.OnWindowFocusChanged\(hasFocus\);"), source);
        var body = Regex.Match(source, @"void OnWindowFocusChanged\(bool hasFocus\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);
        Assert.True(body.Success, "WatchedCallback.OnWindowFocusChanged should have a body that looks at focus gained.");
        Assert.Contains("if (hasFocus)", body.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("ScreenReturned(", body.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("inner.OnWindowFocusChanged(hasFocus)", body.Groups["body"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void AttachAnnouncesTheScreenAsTheCallbacks()
    {
        var source = LogLoopSource.File(TapWatchFile);

        Assert.Matches(new Regex(@"watch\.ScreenShown\(screen \?\? activity\.GetType\(\)\.Name, callback\)"), source);
    }
}
