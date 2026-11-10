using Microsoft.Extensions.Logging;

namespace Storyworthtelling.Maui.Diagnostics;

/// <summary>
/// The three routes a fault takes to a MAUI head's process, each written to the file log the weekly upload carries
/// (audit 08 §19) and handed to the head's own record (its crash.log, or Index's next-launch notice). Folio and Foreword
/// had no hook for a faulted task nobody awaited — twenty-odd <c>async void</c> handlers and discarded tasks — and Index
/// sent two of the three only to files the upload never read.
/// </summary>
public static class MauiCrashHooks
{
    /// <param name="record">The head's own record of a fault: a title and the whole exception text.</param>
    public static void Install(Action<string, string> record)
    {
        var previous = Java.Lang.Thread.DefaultUncaughtExceptionHandler;
        Java.Lang.Thread.DefaultUncaughtExceptionHandler = new JavaThreadHook(previous, record);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            record("Unhandled exception", args.ExceptionObject?.ToString() ?? "(no details)");
            MauiLog.Current?.Write("Crash", LogLevel.Critical, "Unhandled exception", args.ExceptionObject as Exception);
        };

        // A faulted task nobody awaited otherwise vanishes. Observed, so it cannot escalate.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            record("Unobserved task exception", args.Exception.ToString());
            MauiLog.Current?.Write("Crash", LogLevel.Warning, "Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    // The uncaught exceptions on a Java thread, which the managed hook never sees. It records the fault and hands it to
    // the handler that was there before, so the process still dies the way it would have.
    private sealed class JavaThreadHook(Java.Lang.Thread.IUncaughtExceptionHandler? previous, Action<string, string> record)
        : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
    {
        public void UncaughtException(Java.Lang.Thread? thread, Java.Lang.Throwable? ex)
        {
            record($"Uncaught exception on thread '{thread?.Name}'", ex?.ToString() ?? "(no details)");
            MauiLog.Current?.Write("Crash", LogLevel.Critical, $"Uncaught exception on thread '{thread?.Name}'", ex);
            previous?.UncaughtException(thread, ex);
        }
    }
}
