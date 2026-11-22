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

    [Fact]
    public void AThrownPolicyCallIsDescribedByItsJavaClassAndFrames()
    {
        var source = LogLoopSource.File(Policy);
        var body = Regex.Match(source, @"private static string DescribeThrowable\(Java\.Lang\.Throwable e\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);

        Assert.True(body.Success, "PolicyManager.DescribeThrowable not found.");
        Assert.Contains("JavaThrowableText.Describe(", body.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains(".Class.Name", body.Groups["body"].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("GetType().Name", body.Groups["body"].Value, StringComparison.Ordinal);
    }

    // A Log.Warn/Info/Error that is not HeadLog's.
    [GeneratedRegex(@"(?<![A-Za-z])Log\.(?:Warn|Info|Error|Debug)\(")]
    private static partial Regex LogcatOnly();
}
