using Android.App;
using Android.Content;
using Android.Runtime;
using Codex.Domain.Diagnostics;

namespace Codex.Ui;

/// <summary>
/// Captures otherwise-uncaught exceptions to the app's private storage so the
/// cause of a crash survives the process death and can be shown on the next
/// successful launch. Install once from each head's <c>Application.OnCreate</c>;
/// call <see cref="ShowPendingCrash"/> from the launcher activity once its UI is
/// up. There is no ADB or logcat after cutover (CUTOVER.md §17), so this is the
/// only on-device window into a crash.
/// </summary>
public static class CrashReporter
{
    public const string ExtraCrashText = "com.storyworthtelling.focusphone.crash.text";

    private static CrashLogStore? store;
    private static CrashRecorder? recorder;

    public static void Install(Application app, string label) => Install(app, label, null);

    /// <summary>
    /// Installs the handlers and, when <paramref name="log"/> is given, appends every crash to it as well
    /// as keeping the latest for the next-launch popup (LOG-LOOP-PLAN.md §4.4) — so a second crash no
    /// longer erases the first, and every crash rides the log into the outbox.
    /// </summary>
    public static void Install(Application app, string label, DiagnosticLog? log)
    {
        var directory = app.FilesDir?.AbsolutePath;
        if (directory is null)
        {
            return;
        }
        store = new CrashLogStore(directory);
        recorder = new CrashRecorder(label, store, log);

        // Managed exceptions on the UI/JNI path surface here; the process-level
        // AppDomain hook and the task-scheduler hook catch the rest.
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Record(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Record(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Record(e.Exception);
    }

    // CrashRecorder unwraps an unobserved task's AggregateException and never throws from the dying
    // process; it lives in Domain so both of those are tested.
    private static void Record(Exception? exception) => recorder?.Record(exception, DateTimeOffset.Now);

    /// <summary>
    /// If a crash was captured on a previous run, opens the crash viewer and
    /// clears the record so it shows only once. Safe to call every launch.
    /// </summary>
    public static void ShowPendingCrash(Activity current)
    {
        if (store is null || !store.HasCrash)
        {
            return;
        }

        var report = store.ReadLatest();
        store.Clear();
        if (report is null)
        {
            return;
        }

        var intent = new Intent(current, typeof(CrashViewerActivity));
        intent.PutExtra(ExtraCrashText, CrashLogFormatter.Format(report));
        current.StartActivity(intent);
    }
}
