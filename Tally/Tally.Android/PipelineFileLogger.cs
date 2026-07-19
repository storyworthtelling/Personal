using Microsoft.Extensions.Logging;

namespace Tally.Android;

// Captures the background refresh log to a file so it can be viewed and shared from the Settings page
// to diagnose problems (an institution that won't connect, repeated retries, a rate-limit wall).
// Size-capped with a single rolled backup so it never grows without bound. Modeled on Storyboarder's
// PipelineFileLogger.
public sealed class PipelineFileLogger : ILoggerProvider, ILogger
{
    public static readonly PipelineFileLogger Instance = new();

    private const long MaxBytes = 1_000_000;

    private static string LogPath => Path.Combine(FileSystem.AppDataDirectory, "refresh.log");
    private static string BackupPath => LogPath + ".1";

    private readonly Lock fileLock = new();

    public static void Clear()
    {
        try
        {
            File.WriteAllText(LogPath, string.Empty);
            if (File.Exists(BackupPath))
                File.Delete(BackupPath);
        }
        catch { }
    }

    public ILogger CreateLogger(string categoryName)
    {
        // The refresh log is for the app's own diagnostics. Framework categories (EF Core's per-command
        // SQL, MAUI) log at Information and would drown the file, so capture those only at Warning+.
        var isOwn = categoryName.StartsWith("Tally", StringComparison.Ordinal);
        return new CategoryLogger(this, ShortCategory(categoryName), isOwn ? LogLevel.Information : LogLevel.Warning);
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter) =>
        Write(null, logLevel, formatter(state, exception), exception);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Dispose() { }

    private void Write(string? category, LogLevel logLevel, string message, Exception? exception)
    {
        if (!IsEnabled(logLevel)) return;

        var prefix = logLevel switch
        {
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT",
            _ => "INFO"
        };

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{prefix}]" +
                   (category is null ? " " : $" {category}: ") + message;
        if (exception != null)
            line += $"\n       {exception.GetType().Name}: {exception.Message}";

        try
        {
            lock (fileLock)
            {
                RollIfTooLarge();
                File.AppendAllText(LogPath, line + "\n");
            }
        }
        catch { }
    }

    private static void RollIfTooLarge()
    {
        try
        {
            if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxBytes)
                return;
            if (File.Exists(BackupPath))
                File.Delete(BackupPath);
            File.Move(LogPath, BackupPath);
        }
        catch { }
    }

    private static string ShortCategory(string categoryName)
    {
        var lastDot = categoryName.LastIndexOf('.');
        return lastDot >= 0 && lastDot < categoryName.Length - 1
            ? categoryName[(lastDot + 1)..]
            : categoryName;
    }

    private sealed class CategoryLogger(PipelineFileLogger owner, string category, LogLevel minLevel) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => logLevel >= minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            owner.Write(category, logLevel, formatter(state, exception), exception);
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
