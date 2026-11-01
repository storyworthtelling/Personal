using Storyworthtelling.Maui.Diagnostics;
using global::Android.App;
using global::Android.Runtime;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

// Declare permissions via assembly attributes so MAUI's manifest merger
// always includes them, even if the XML manifest is partially overwritten.
[assembly: UsesPermission(global::Android.Manifest.Permission.Internet)]
[assembly: UsesPermission(global::Android.Manifest.Permission.ReceiveBootCompleted)]
[assembly: UsesPermission(global::Android.Manifest.Permission.WakeLock)]
[assembly: UsesPermission("android.permission.POST_NOTIFICATIONS")]
[assembly: UsesPermission("android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS")]

namespace Foreword.Android.Platforms.Android;

// The launcher icon is set here, not in Platforms/Android/AndroidManifest.xml: this head does not set
// <SingleProject>true</SingleProject>, so MAUI never wires that XML manifest into the build (the same
// reason the permissions above are declared as assembly attributes). Setting Icon/RoundIcon on
// [Application] bakes android:icon into the generated manifest, and the mipmap resources under
// Resources/ (the active resource prefix) resolve it. Regenerate the icon via Tools/IconStudio.
[Application(Icon = "@mipmap/ic_launcher", RoundIcon = "@mipmap/ic_launcher_round")]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        var prevHandler = Java.Lang.Thread.DefaultUncaughtExceptionHandler;
        Java.Lang.Thread.DefaultUncaughtExceptionHandler = new CrashLogger(prevHandler);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            CrashLogger.Log(args.ExceptionObject?.ToString() ?? "(no details)");
            // The same fault, in the file log the weekly upload carries (LOG-LOOP-PLAN.md §4.4).
            MauiLog.Current?.Write("Unhandled", Microsoft.Extensions.Logging.LogLevel.Critical, "Unhandled exception", args.ExceptionObject as Exception);
        };

        base.OnCreate();
    }

    private sealed class CrashLogger : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
    {
        private const string LogTag = "Foreword";
        private readonly Java.Lang.Thread.IUncaughtExceptionHandler? previous;

        public CrashLogger(Java.Lang.Thread.IUncaughtExceptionHandler? previous)
        {
            this.previous = previous;
        }

        public void UncaughtException(Java.Lang.Thread? thread, Java.Lang.Throwable? ex)
        {
            Log($"Uncaught exception on thread '{thread?.Name}':\n{ex}");
            previous?.UncaughtException(thread, ex);
        }

        internal static void Log(string message)
        {
            global::Android.Util.Log.Error(LogTag, message);
            try
            {
                var path = global::System.IO.Path.Combine(
                    global::Android.App.Application.Context.FilesDir!.AbsolutePath,
                    "crash.log");
                global::System.IO.File.AppendAllText(
                    path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{message}\n\n---\n\n");
            }
            catch { }
        }
    }
}
