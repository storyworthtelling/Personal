using Microsoft.Extensions.Logging;

namespace Storyboarder.Android;

// Captures the background pipeline's log output to a file so it can be viewed and shared from the
// Settings page to diagnose problems (a stalled conversion, repeated retries, a rate-limit wall).
// Modeled on DailyLoader's DeliveryFileLogger, but the Storyboarder pipeline runs for days, so the
// file is size-capped with a single rolled backup instead of growing without bound.
public sealed class PipelineFileLogger : ILoggerProvider, ILogger
{
    public static readonly PipelineFileLogger Instance = new();

    // Keep the live log small enough to open on a phone; roll one backup so recent history survives.
    private const long MaxBytes = 1_500_000;

    private static string LogPath => Path.Combine(FileSystem.AppDataDirectory, "pipeline.log");
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
        // The pipeline log is for the app's own diagnostics. Framework categories (EF Core's per-command
        // SQL, MAUI) log at Information and would otherwise drown the file — the 5-second UI refresh alone
        // emits ~20 SELECTs — so capture those only at Warning+, keeping their errors without the noise.
        var isOwn = categoryName.StartsWith("Storyboarder", StringComparison.Ordinal);
        return new CategoryLogger(this, ShortCategory(categoryName), isOwn ? LogLevel.Information : LogLevel.Warning);
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    // The provider itself logs without a category (used if resolved directly).
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

    // When the live log passes the cap, move it to the single backup slot and start fresh.
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

    // Trim the namespace so lines read "GraphicNovelProcessor" rather than the full type name.
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
