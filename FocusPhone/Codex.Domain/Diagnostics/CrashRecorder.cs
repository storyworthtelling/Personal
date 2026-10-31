namespace Codex.Domain.Diagnostics;

/// <summary>
/// What every head's crash handler does with an uncaught exception (LOG-LOOP-PLAN.md §4.4, G5): keep it
/// for the one-shot next-launch popup (<see cref="CrashLogStore"/>, latest only) <b>and</b> append it to the
/// rolling <see cref="DiagnosticLog"/>, so a second crash no longer erases the first and every crash
/// reaches the outbox. Post did the append on its own before; this is that behaviour for every head.
/// <para>
/// Android-free so it is tested here; <c>Codex.Ui.CrashReporter</c> only wires the platform's three
/// unhandled-exception hooks to <see cref="Record"/>. It runs inside an already-dying process, so it never
/// throws: losing the record beats a second exception that masks the first.
/// </para>
/// </summary>
public sealed class CrashRecorder(string appLabel, CrashLogStore store, DiagnosticLog? log)
{
    public void Record(Exception? exception, DateTimeOffset when)
    {
        if (exception is null)
        {
            return;
        }

        // An unobserved task exception arrives wrapped in an AggregateException whose own stack trace is
        // empty (it was never thrown); unwrap a single inner exception so the real fault is recorded.
        if (exception is AggregateException aggregate)
        {
            var flattened = aggregate.Flatten();
            exception = flattened.InnerExceptions.Count == 1 ? flattened.InnerExceptions[0] : flattened;
        }

        try
        {
            store.Save(new CrashReport(
                when,
                appLabel,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message,
                exception.StackTrace ?? string.Empty));
        }
        catch
        {
            // See the class comment: never throw from the crash path.
        }

        try
        {
            log?.Append(DiagnosticLogFormatter.FromException(
                when, DiagnosticLevel.Crash, appLabel, "Unhandled exception", exception));
        }
        catch
        {
            // As above.
        }
    }
}
