using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// What every native head on the log loop has (plan §3, §4.3, §4.4), read by content: the shared diagnostics
/// installed once from its Application (outbox, crash reporter appending to the log, tap watch, launch line,
/// exit reasons, the weekly upload), every log it writes sealing into the outbox rather than discarding, and
/// a way to Send logs now.
/// </summary>
public partial class HeadsOnTheLoopTests
{
    [Theory]
    [MemberData(nameof(LogLoopHeads.Apps), MemberType = typeof(LogLoopHeads))]
    public void TheApplicationInstallsTheSharedDiagnosticsOnce(string app)
    {
        var head = LogLoopHeads.For(app);
        var applications = LogLoopSource.Sources(head.Folder).Where(s => ApplicationDeclaration().IsMatch(s.Text)).ToList();

        var application = Assert.Single(applications);
        Assert.Contains("HeadDiagnostics.Install(", application.Text, StringComparison.Ordinal);
        Assert.Contains($"\"{app}\"", application.Text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(LogLoopHeads.Folders), MemberType = typeof(LogLoopHeads))]
    public void EveryLogTheHeadWritesSealsIntoTheOutbox(string folder)
    {
        var unsealed = LogLoopSource.Sources(folder)
            .SelectMany(s => NewLog().Matches(s.Text).Where(m => !m.Value.Contains("utbox", StringComparison.Ordinal)).Select(m => $"{s.Path}: {m.Value}"))
            .ToList();

        Assert.True(unsealed.Count == 0, "A log that discards its older half instead of sealing it:\n" + string.Join("\n", unsealed));
    }

    [Theory]
    [MemberData(nameof(LogLoopHeads.Folders), MemberType = typeof(LogLoopHeads))]
    public void NoHeadKeepsItsOwnCopyOfTheCrashHooks(string folder)
    {
        var hooked = LogLoopSource.Sources(folder)
            .Where(s => s.Text.Contains("AndroidEnvironment.UnhandledExceptionRaiser", StringComparison.Ordinal))
            .Select(s => s.Path)
            .ToList();

        Assert.True(hooked.Count == 0, "Two sets of hooks log every crash twice; the shared reporter appends it:\n" + string.Join("\n", hooked));
    }

    [Theory]
    [MemberData(nameof(LogLoopHeads.Apps), MemberType = typeof(LogLoopHeads))]
    public void SendLogsNowIsReachable(string app)
    {
        var head = LogLoopHeads.For(app);
        var text = string.Concat(LogLoopSource.Sources(head.Folder).Select(s => s.Text));

        Assert.True(
            text.Contains("LoopDiagnosticsActivity", StringComparison.Ordinal) || text.Contains("\"Send logs now\"", StringComparison.Ordinal),
            $"{app} has no way to Send logs now.");
    }

    [Fact]
    public void TheSharedUploadWorkerKeepsItsRegisteredNames()
    {
        var worker = LogLoopSource.File("FocusPhone/Codex.Ui.Android/Diagnostics/LoopUploadWorker.cs");

        Assert.Contains("Register(\"com.storyworthtelling.shared.LoopUploadWorker\")]", worker, StringComparison.Ordinal);
        Assert.Contains("WorkName = \"log-upload\"", worker, StringComparison.Ordinal);
        Assert.Contains("NetworkType.Connected", worker, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromDays(1)", worker, StringComparison.Ordinal);
        Assert.Contains("ExistingPeriodicWorkPolicy.Keep", worker, StringComparison.Ordinal);
        Assert.Contains("UploadWindow.Due(", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSharedLayerDoesWhatEveryHeadNeeds()
    {
        var shared = LogLoopSource.File("FocusPhone/Codex.Ui.Android/Diagnostics/HeadDiagnostics.cs");

        foreach (var step in new[] { "new LogOutbox(", "CrashReporter.Install(", "TapWatch.Install(", "ExitTrail.Record(", "LaunchLine.Format(", "LogPhoneRole.", "new LogUploader(" })
        {
            Assert.Contains(step, shared, StringComparison.Ordinal);
        }
    }

    [GeneratedRegex(@"class\s+\w+\s*(?:\([^)]*\))?\s*:\s*(?:Android\.App\.)?Application\b")]
    private static partial Regex ApplicationDeclaration();

    [GeneratedRegex(@"new DiagnosticLog\([^;]*?\)\s*[;,)]")]
    private static partial Regex NewLog();
}
