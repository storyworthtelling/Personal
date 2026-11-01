using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// LOG-LOOP-PLAN.md §4.5 (Flyleaf): the device-owner policy pass is the part of the phone most likely to fail
/// quietly — a restriction refused, a package that would not suspend, private DNS not set — and its warnings were
/// logcat-only, which no one reads. Every line it writes now goes through the head log as well, so a failed pass
/// lands in the weekly upload.
/// </summary>
public partial class FlyleafDiagnosticsTests
{
    private const string Policy = "FocusPhone/Flyleaf.Android/PolicyManager.cs";

    [Fact]
    public void ThePolicyPassWritesEveryLineToTheHeadLog()
    {
        var source = LogLoopSource.File(Policy);
        var logcatOnly = LogcatOnly().Matches(source).Select(m => m.Value).ToList();

        Assert.True(logcatOnly.Count == 0, "Policy lines that reach logcat only:\n" + string.Join("\n", logcatOnly));
        Assert.Contains("HeadLog.Warn(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePolicySummaryIsLogged()
    {
        Assert.Contains("HeadLog.Info(Tag, $\"Policy applied:", LogLoopSource.File(Policy), StringComparison.Ordinal);
    }

    // A Log.Warn/Info/Error that is not HeadLog's.
    [GeneratedRegex(@"(?<![A-Za-z])Log\.(?:Warn|Info|Error|Debug)\(")]
    private static partial Regex LogcatOnly();
}
