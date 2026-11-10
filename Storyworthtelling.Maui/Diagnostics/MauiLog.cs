namespace Storyworthtelling.Maui.Diagnostics;

/// <summary>
/// The head's one file log, installed once from <c>MauiProgram</c> and reachable from the places that are not
/// in the logging pipeline: the unhandled-exception handlers in <c>MainApplication</c>, the Settings screen
/// that shows and clears it, and the log loop's upload.
/// </summary>
public static class MauiLog
{
    public static FileLogger? Current { get; private set; }

    /// <summary>A trail for a shared library that reports a problem flag and its words (the vault's, say).</summary>
    public static Action<bool, string> Trail(string category) => (problem, message) =>
        Current?.Write(category, problem ? Microsoft.Extensions.Logging.LogLevel.Warning : Microsoft.Extensions.Logging.LogLevel.Information, message, null);

    /// <summary>Installs the head's file log and returns it, to hand to <c>builder.Logging.AddProvider</c>.</summary>
    public static FileLogger Install(FileLoggerOptions options)
    {
        var logger = new FileLogger(options);
        Current = logger;
        return logger;
    }
}
