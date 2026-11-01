using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// What is Post's own on the loop (plan §3, §4.5), read by content: its upload worker, which shipped first and
/// is registered under names that must not move, Send logs now on its own Diagnostics screen, and every §4.5
/// line written somewhere. What every head shares is <see cref="HeadsOnTheLoopTests"/>.
/// </summary>
public class PostLogLoopWiringTests
{
    private const string Head = "FocusPhone/Post.Android";

    private static string HeadText() => string.Concat(LogLoopSource.Sources(Head).Select(s => s.Text));

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

}
