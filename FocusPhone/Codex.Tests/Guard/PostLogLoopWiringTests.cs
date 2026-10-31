using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Post's half of Phase 1 (plan §3, §4.3–4.5), read by content: the logs adopt the outbox, the weekly
/// upload worker is registered under names that must not move, Send logs now exists, exit reasons and the
/// launch line are read on start, and every §4.5 line is written somewhere.
/// </summary>
public class PostLogLoopWiringTests
{
    private const string Head = "FocusPhone/Post.Android";

    private static string HeadText() => string.Concat(LogLoopSource.Sources(Head).Select(s => s.Text));

    [Fact]
    public void BothPostLogsSealIntoTheOutbox()
    {
        var diagnostics = LogLoopSource.File($"{Head}/PostDiagnostics.cs");

        Assert.Contains("new LogOutbox(", diagnostics, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(diagnostics, ", outbox)"));
    }

    [Fact]
    public void CrashesGoThroughTheSharedReporterIntoThePostLog()
    {
        var application = LogLoopSource.File($"{Head}/PostApplication.cs");
        var diagnostics = LogLoopSource.File($"{Head}/PostDiagnostics.cs");

        Assert.Contains("CrashReporter.Install(this, \"Post\", PostDiagnostics.Log)", application, StringComparison.Ordinal);
        // Post's own copy of the three hooks is gone: two sets would log every crash twice.
        Assert.DoesNotContain("UnhandledExceptionRaiser", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUploadWorkerKeepsItsRegisteredNames()
    {
        var worker = LogLoopSource.File($"{Head}/LogUploadWorker.cs");

        Assert.Contains("Register(\"com.storyworthtelling.focusphone.communication.LogUploadWorker\")]", worker, StringComparison.Ordinal);
        Assert.Contains("WorkName = \"log-upload\"", worker, StringComparison.Ordinal);
        Assert.Contains("NetworkType.Connected", worker, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromDays(1)", worker, StringComparison.Ordinal);
        Assert.Contains("ExistingPeriodicWorkPolicy.Keep", worker, StringComparison.Ordinal);
        Assert.Contains("UploadWindow.Due(", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUploadIsScheduledAtStartAndSendLogsNowIsOnTheDiagnosticsScreen()
    {
        Assert.Contains("LogUploadWorker.Schedule(", LogLoopSource.File($"{Head}/PostApplication.cs"), StringComparison.Ordinal);
        var screen = LogLoopSource.File($"{Head}/DiagnosticsActivity.cs");
        Assert.Contains("\"Send logs now\"", screen, StringComparison.Ordinal);
        Assert.Contains("TapWatch.Action(PostKeyActions.SendLogsNow", screen, StringComparison.Ordinal);
    }

    [Fact]
    public void ExitReasonsAndTheLaunchLineAreReadOnStart()
    {
        var application = LogLoopSource.File($"{Head}/PostApplication.cs");

        Assert.Contains("ExitTrail.Record(", application, StringComparison.Ordinal);
        Assert.Contains("GetHistoricalProcessExitReasons", LogLoopSource.File("FocusPhone/Codex.Ui.Android/Diagnostics/ExitTrail.cs"), StringComparison.Ordinal);
        Assert.Contains("LaunchLine.Format(", HeadText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PostLogLines.SmsRole(")]
    [InlineData("PostLogLines.SmsSent(")]
    [InlineData("PostLogLines.MmsSent(")]
    [InlineData("PostLogLines.MmsDownloaded(")]
    [InlineData("PostLogLines.MailRoute(")]
    [InlineData("PostLogLines.OwedDeleteFailing(")]
    public void EveryPostLineIsWritten(string call)
    {
        Assert.Contains(call, HeadText(), StringComparison.Ordinal);
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
