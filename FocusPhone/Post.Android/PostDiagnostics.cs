using Android.Content;
using Codex.Domain.Diagnostics;
using Codex.Domain.Post;
using Codex.Ui;
using Storyworthtelling.Diagnostics;

namespace Post;

/// <summary>
/// The Post head's diagnostics layer: a rolling, reviewable <see cref="DiagnosticLog"/> of non-fatal
/// failures (background email sync, the message bridge, timeline-store problems) that never crash the app,
/// every crash (through the shared <see cref="Ui.CrashReporter"/>), and the texts trail — reviewed and
/// shared from Settings → Diagnostics. There is no ADB or logcat after cutover (CUTOVER.md §17), so this
/// on-device log is the only window into these faults.
///
/// On the log loop (LOG-LOOP-PLAN.md, Phase 1 pilot): both logs seal into the <see cref="LogOutbox"/>
/// rather than discarding their older half, the weekly <see cref="LogUploadWorker"/> carries the sealed
/// segments to Drive and clears them once Drive has a confirmed copy, and the tap watch, the Gmail sign-in
/// trail, exit reasons and the worker trail all write here.
///
/// The machinery is the shared <see cref="HeadDiagnostics"/>, installed first thing from
/// <see cref="PostApplication.OnCreate"/>; this is Post's own surface over it. The log is rooted at the app's
/// FilesDir, which the WorkManager sync process shares with the UI process, so background failures land in
/// the same file the screen reads.
/// </summary>
public static class PostDiagnostics
{
    public const string Label = "Post";

    /// <summary>The app's name in every batch header and file name.</summary>
    public const string LoopApp = "post";

    // The texts trail: a line per text that reaches Post, by whichever route, so a late or
    // missing text can be traced afterwards. Its own file and cap, because a line per text
    // in the failure log would roll the failure history out within a day.
    public const string TextsFileName = "texts.log";
    public const long TextsMaxBytes = 96 * 1024;

    private static HeadDiagnostics? Head => HeadDiagnostics.Current;

    /// <summary>The failure log, which the crash reporter appends every crash to.</summary>
    public static DiagnosticLog? Log => Head?.Log;

    /// <summary>Where sealed segments wait for Drive.</summary>
    public static LogOutbox? Outbox => Head?.Outbox;

    /// <summary>Every live log the upload seals.</summary>
    public static IReadOnlyList<string> LivePaths => Head?.LivePaths ?? [];

    /// <summary>
    /// Records a non-fatal failure that would otherwise be swallowed into an
    /// inaccessible logcat. Callers keep their existing <c>Android.Util.Log</c>
    /// line as a live-debugging mirror; this is the durable, shareable copy.
    /// </summary>
    public static void LogWarning(string source, string message, Exception? exception = null) =>
        Head?.Warn(source, message, exception);

    /// <summary>A decision or outcome worth having in the weekly record (plan §4.5) that is not a failure.</summary>
    public static void LogInfo(string source, string message) => Head?.Info(source, message);

    /// <summary>A finished sign-in trail and its diagnosis (plan §4.2).</summary>
    public static void LogSignIn(SignInTrail trail) => Head?.SignIn(trail);

    /// <summary>Runs a worker's body on the worker trail (plan §4.3), rethrowing what it throws.</summary>
    public static T Worker<T>(string name, Func<T> body) => HeadDiagnostics.Worker(name, body);

    /// <summary>
    /// Adds a line to the texts trail. The line is built by <see cref="TextTrail"/>, which
    /// keeps message content and whole numbers out of it — the trail is meant to be shared.
    /// </summary>
    public static void LogText(string source, string line) => Head?.Line(TextsFileName, source, line);

    public static bool HasEntries => Head?.HasEntries ?? false;

    /// <summary>Everything since the last confirmed upload: sealed segments, then the live file.</summary>
    public static string ReadAll() => Head?.ReadAll() ?? string.Empty;

    public static void Clear() => Head?.Clear();

    /// <summary>Reverses a <see cref="Clear"/> within the undo window (DESIGN.md §7).</summary>
    public static void Restore(string content) => Head?.Restore(content);

    /// <summary>
    /// The shareable export bundle: a device snapshot, the full rolling log, the latest
    /// captured crash, the field notes, and the texts trail with the texts status.
    /// </summary>
    public static string BuildExport(Context context)
    {
        var report = Head?.BuildExport() ?? string.Empty;
        // Field notes ride along with the failures. They are what turns one run of the
        // on-device test plan into the numbers the deferred work needs — how much of the
        // owner's real mail is HTML-only, what their attachments are, how far the archive
        // grows, how many contacts have no face, and whether date detection finds anything
        // worth proposing.
        return $"{report}\n\n{FieldNotesStore.Load(context).Render()}\n\n{TextsStatus(context)}";
    }

    // Which routes a text has into Post right now (the trail itself is in the export above).
    private static string TextsStatus(Context context)
    {
        try
        {
            return "=== Texts ===\n" + TextsStatusReader.Read(context).Render();
        }
        catch (Exception e)
        {
            return $"=== Texts ===\nCould not read the texts status: {e.Message}";
        }
    }
}
