using Microsoft.Extensions.Logging;

namespace Folio.Tests.Support;

// Keeps every entry a component logs, so a test can assert that a failure was recorded — and with what.
public sealed class CapturingLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    private readonly List<Entry> entries = [];

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (entries)
                return entries.ToList();
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (entries)
            entries.Add(new Entry(logLevel, formatter(state, exception), exception));
    }
}
