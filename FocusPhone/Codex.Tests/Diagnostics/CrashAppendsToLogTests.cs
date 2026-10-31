using Codex.Domain.Diagnostics;
using Xunit;

namespace Codex.Tests.Diagnostics;

// Plan §4.4 (G5): only the latest crash was kept, overwritten by the next. CrashRecorder — what every
// head's CrashReporter now calls — still feeds the one-shot next-launch popup, and also appends every crash
// to the rolling log, so a second crash no longer erases the first and both reach the outbox.
public class CrashAppendsToLogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "focusphone-crash-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static readonly DateTimeOffset When = new(2026, 10, 30, 9, 15, 0, TimeSpan.Zero);

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    [Fact]
    public void TwoCrashesBothReachTheLog()
    {
        var log = new DiagnosticLog(directory);
        var recorder = new CrashRecorder("Post", new CrashLogStore(directory), log);

        recorder.Record(Thrown(new InvalidOperationException("first fault")), When);
        recorder.Record(Thrown(new ArgumentException("second fault")), When.AddMinutes(5));

        var all = log.ReadAll();
        Assert.Contains("CRASH Post — Unhandled exception", all);
        Assert.Contains("first fault", all);
        Assert.Contains("second fault", all);
        Assert.Contains("CrashAppendsToLogTests.Thrown", all); // the stack trace came too
    }

    [Fact]
    public void TheOneShotPopupStillShowsTheLatest()
    {
        var store = new CrashLogStore(directory);
        var recorder = new CrashRecorder("Post", store, new DiagnosticLog(directory));

        recorder.Record(Thrown(new InvalidOperationException("first fault")), When);
        recorder.Record(Thrown(new ArgumentException("second fault")), When.AddMinutes(5));

        var latest = store.ReadLatest();
        Assert.NotNull(latest);
        Assert.Equal("second fault", latest!.Message);
        Assert.Equal("Post", latest.AppLabel);
    }

    [Fact]
    public void AnUnobservedTaskFaultIsUnwrappedToTheRealException()
    {
        var store = new CrashLogStore(directory);
        var log = new DiagnosticLog(directory);
        var recorder = new CrashRecorder("Post", store, log);

        recorder.Record(new AggregateException(Thrown(new TimeoutException("the real fault"))), When);

        Assert.Equal(typeof(TimeoutException).FullName, store.ReadLatest()!.ExceptionType);
        Assert.Contains("System.TimeoutException: the real fault", log.ReadAll());
    }

    [Fact]
    public void WithoutALogTheStoreStillWorks()
    {
        var store = new CrashLogStore(directory);

        new CrashRecorder("Score", store, log: null).Record(Thrown(new InvalidOperationException("x")), When);

        Assert.True(store.HasCrash);
    }

    [Fact]
    public void ARecorderThatCannotWriteNeverThrowsFromTheDyingProcess()
    {
        Directory.CreateDirectory(directory);
        // A file where the log's directory should be makes every write fail.
        var blocked = Path.Combine(directory, "blocked");
        File.WriteAllText(blocked, "not a directory");
        var recorder = new CrashRecorder("Post", new CrashLogStore(blocked), new DiagnosticLog(blocked));

        var thrown = Record.Exception(() => recorder.Record(Thrown(new InvalidOperationException("x")), When));

        Assert.Null(thrown);
    }

    [Fact]
    public void ANullExceptionIsIgnored()
    {
        var store = new CrashLogStore(directory);

        new CrashRecorder("Post", store, new DiagnosticLog(directory)).Record(null, When);

        Assert.False(store.HasCrash);
    }
}
