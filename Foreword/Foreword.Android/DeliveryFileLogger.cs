using Microsoft.Extensions.Logging;

namespace Foreword.Android;

// Captures log output to a file so delivery diagnostics are readable in the app UI.
public sealed class DeliveryFileLogger : ILoggerProvider, ILogger
{
    public static readonly DeliveryFileLogger Instance = new();

    private static string LogPath =>
        Path.Combine(FileSystem.AppDataDirectory, "delivery.log");

    private readonly Lock fileLock = new();

    public static void Clear()
    {
        try { File.WriteAllText(LogPath, string.Empty); }
        catch { }
    }

    public ILogger CreateLogger(string categoryName) => this;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var prefix = logLevel switch
        {
            LogLevel.Warning  => "WARN",
            LogLevel.Error    => "ERROR",
            LogLevel.Critical => "CRIT",
            _                 => "INFO"
        };

        var line = $"[{prefix}] {formatter(state, exception)}";
        if (exception != null)
            line += $"\n       {exception.GetType().Name}: {exception.Message}";

        try
        {
            lock (fileLock)
                File.AppendAllText(LogPath, line + "\n");
        }
        catch { }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Dispose() { }
}
