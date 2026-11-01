using Codex.Ui;
using Android.Content;

namespace Score;

/// <summary>
/// Score's diagnostics layer, on top of the shared one-shot
/// <see cref="Codex.Ui.CrashReporter"/>. It keeps a rolling, reviewable
/// <see cref="DiagnosticLog"/> of non-fatal failures — a play that couldn't start
/// (the media controller still connecting, or an episode with no playable audio)
/// and an auto-arrange that found nothing to queue — plus a copy of each unhandled
/// crash, so the owner can review and share a history from Manage → Diagnostics.
/// There is no ADB or logcat after cutover (CUTOVER.md §17), so this on-device log
/// is the only window into these faults.
///
/// The machinery is the shared <see cref="HeadDiagnostics"/>, installed from
/// <see cref="AudioApplication.OnCreate"/>; this is Score's surface over it. The log is rooted at the
/// app's FilesDir, which the playback service process shares with the UI process, so a background
/// failure lands in the same file the screen reads.
/// </summary>
public static class AudioDiagnostics
{
    /// <summary>
    /// Records a non-fatal failure that would otherwise be swallowed into an
    /// inaccessible logcat. The user sees a brief toast; this is the durable,
    /// shareable copy, carrying the full stack trace of any passed exception.
    /// </summary>
    public static void LogWarning(string source, string message, Exception? exception = null) =>
        HeadDiagnostics.Current?.Warn(source, message, exception);

    /// <summary>A decision or outcome worth having in the weekly record that is not a failure.</summary>
    public static void LogInfo(string source, string message) => HeadDiagnostics.Current?.Info(source, message);

    public static bool HasEntries => HeadDiagnostics.Current?.HasEntries ?? false;

    public static string ReadAll() => HeadDiagnostics.Current?.ReadAll() ?? string.Empty;

    public static void Clear() => HeadDiagnostics.Current?.Clear();

    /// <summary>Reverses a <see cref="Clear"/> within the undo window (DESIGN.md §7).</summary>
    public static void Restore(string content) => HeadDiagnostics.Current?.Restore(content);

    /// <summary>The shareable export bundle: a device snapshot, the rolling log, and the latest crash.</summary>
    public static string BuildExport(Context context) => HeadDiagnostics.Current?.BuildExport() ?? string.Empty;
}
