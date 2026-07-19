using global::Android.App;
using global::Android.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Ledger.Infrastructure.Persistence;

// Declare permissions via assembly attributes so MAUI's manifest merger always includes them.
[assembly: UsesPermission(global::Android.Manifest.Permission.Internet)]
[assembly: UsesPermission(global::Android.Manifest.Permission.ReceiveBootCompleted)]
[assembly: UsesPermission(global::Android.Manifest.Permission.WakeLock)]
[assembly: UsesPermission("android.permission.POST_NOTIFICATIONS")]

namespace Ledger.Android.Platforms.Android;

// The launcher icon is set here, not in Platforms/Android/AndroidManifest.xml: this head does not set
// <SingleProject>true</SingleProject>, so MAUI never wires that XML manifest into the build (the same
// reason the permissions above are declared as assembly attributes). Setting Icon/RoundIcon on
// [Application] bakes android:icon into the generated manifest, and the mipmap resources under
// Resources/ (the active resource prefix) resolve it. Regenerate the icon via Tools/IconStudio.
[Application(Icon = "@mipmap/appicon", RoundIcon = "@mipmap/appicon_round")]
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
            CrashLogger.Log(args.ExceptionObject?.ToString() ?? "(no details)");

        base.OnCreate();

        // Apply EF Core migrations once at process start (the process is created for WorkManager jobs
        // too), so the SQLite schema always exists before the worker or UI touches it.
        RunMigrations();
    }

    // Brings the on-device schema up to date. Records the outcome in SettingsStore so a failed update
    // surfaces on the Settings page rather than silently stalling every worker cycle.
    internal static void RunMigrations()
    {
        try
        {
            var factory = IPlatformApplication.Current!.Services
                .GetRequiredService<IDbContextFactory<LedgerDbContext>>();
            using var db = factory.CreateDbContext();
            db.Database.Migrate();
            SettingsStore.LastMigrationError = string.Empty;
        }
        catch (Exception ex)
        {
            SettingsStore.LastMigrationError = ex.Message;
            CrashLogger.Log($"Database migration failed:\n{ex}");
        }
    }

    private sealed class CrashLogger : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
    {
        private const string LogTag = "Ledger";
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
