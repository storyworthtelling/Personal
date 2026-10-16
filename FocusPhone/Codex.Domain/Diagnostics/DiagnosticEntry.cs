namespace Codex.Domain.Diagnostics;

/// <summary>
/// A single non-fatal (or crash) diagnostic record appended to the rolling
/// <see cref="DiagnosticLog"/>. Unlike <see cref="CrashReport"/> — which is the
/// one-shot "why did the app die last time" popup — these accumulate so the
/// owner can review a history of failures that never crashed the app but explain
/// why something (a background email sync, the message bridge) silently stopped
/// working. There is no ADB or logcat after cutover (CUTOVER.md §17), so this
/// on-device log is the only window into those failures.
/// </summary>
public record DiagnosticEntry(
    DateTimeOffset When,
    string Level,
    string Source,
    string Message,
    string? Detail = null);

/// <summary>Severity labels for <see cref="DiagnosticEntry.Level"/>.</summary>
public static class DiagnosticLevel
{
    public const string Info = "INFO";
    public const string Warning = "WARN";
    public const string Error = "ERROR";
    public const string Crash = "CRASH";
}
