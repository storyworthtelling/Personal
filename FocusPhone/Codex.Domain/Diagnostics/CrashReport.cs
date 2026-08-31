namespace Codex.Domain.Diagnostics;

/// <summary>
/// A single captured crash. Persisted to the crashing app's private storage so
/// it can be surfaced on the device's next successful launch — the phone has no
/// ADB or logcat access after cutover (CUTOVER.md §17), so an on-device record
/// is the only way to see why a head died.
/// </summary>
public record CrashReport(
    DateTimeOffset When,
    string AppLabel,
    string ExceptionType,
    string Message,
    string StackTrace);
