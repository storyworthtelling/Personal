using global::Android.App;
using global::Android.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Index.Infrastructure.Persistence;

// Declare permissions via assembly attributes so MAUI's manifest merger always includes them.
[assembly: UsesPermission(global::Android.Manifest.Permission.Internet)]
[assembly: UsesPermission(global::Android.Manifest.Permission.ReceiveBootCompleted)]
[assembly: UsesPermission(global::Android.Manifest.Permission.WakeLock)]
[assembly: UsesPermission("android.permission.POST_NOTIFICATIONS")]

namespace Index.Android.Platforms.Android;

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

        // Every route a fault can take to the process lands in LaunchFaults.Record, so the next launch
        // shows it whether it killed the app or was only logged.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LaunchFaults.Record("Unhandled exception", args.ExceptionObject?.ToString() ?? "(no details)");

        // A faulted Task nobody awaited (e.g. a fire-and-forget background refresh) otherwise vanishes;
        // capture it so a silent failure still lands in the crash log. Mark it observed so it can't escalate.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LaunchFaults.Record("Unobserved task exception", args.Exception.ToString());
            args.SetObserved();
        };

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
            LaunchFaults.Record("Database migration", ex);
        }
    }

    // The Java-side handler: the uncaught exceptions the managed hook does not see on the Android
    // thread. It records the fault, then hands it to the handler that was there before, so the process
    // still dies the way it would have.
    private sealed class CrashLogger : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
    {
        private readonly Java.Lang.Thread.IUncaughtExceptionHandler? previous;

        public CrashLogger(Java.Lang.Thread.IUncaughtExceptionHandler? previous)
        {
            this.previous = previous;
        }

        public void UncaughtException(Java.Lang.Thread? thread, Java.Lang.Throwable? ex)
        {
            LaunchFaults.Record($"Uncaught exception on thread '{thread?.Name}'", ex?.ToString() ?? "(no details)");
            previous?.UncaughtException(thread, ex);
        }
    }
}
