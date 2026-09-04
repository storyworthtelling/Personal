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
    private static string appLabel = "FocusPhone";

    public static void Install(Application app, string label)
    {
        appLabel = label;

        var directory = app.FilesDir?.AbsolutePath;
        if (directory is null)
        {
            return;
        }
        store = new CrashLogStore(directory);

        // Managed exceptions on the UI/JNI path surface here; the process-level
        // AppDomain hook and the task-scheduler hook catch the rest.
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Record(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Record(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Record(e.Exception);
    }

    private static void Record(Exception? exception)
    {
        if (exception is null || store is null)
        {
            return;
        }

        // An unobserved task exception arrives wrapped in an AggregateException
        // whose own StackTrace is empty (it was never thrown), which is why a
        // crash surfaced this way reads as "no stack trace". Unwrap a single
        // inner exception so the real fault and its stack trace are recorded.
        if (exception is AggregateException aggregate)
        {
            var flattened = aggregate.Flatten();
            exception = flattened.InnerExceptions.Count == 1 ? flattened.InnerExceptions[0] : flattened;
        }

        try
        {
            store.Save(new CrashReport(
                DateTimeOffset.Now,
                appLabel,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message,
                exception.StackTrace ?? string.Empty));
        }
        catch
        {
            // The handler runs inside an already-dying process: never let a
            // logging failure throw and mask the original crash. Losing the
            // record is preferable to a second exception in the handler.
        }
    }

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
