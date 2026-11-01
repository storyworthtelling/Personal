using Storyworthtelling.Maui.Diagnostics;
using global::Android.App;
using global::Android.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Folio.Infrastructure.Persistence;

// Declare permissions via assembly attributes so MAUI's manifest merger always includes them.
[assembly: UsesPermission(global::Android.Manifest.Permission.Internet)]
[assembly: UsesPermission(global::Android.Manifest.Permission.ReceiveBootCompleted)]
[assembly: UsesPermission(global::Android.Manifest.Permission.WakeLock)]
[assembly: UsesPermission("android.permission.POST_NOTIFICATIONS")]
[assembly: UsesPermission("android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS")]
// Reading a novel takes about a quarter of an hour of model calls (F49, measured), and a background
// worker is killed at ten minutes. These two let the pipeline promote itself to the foreground for
// the duration — which is also the honest thing to show, since the phone really is busy.
[assembly: UsesPermission("android.permission.FOREGROUND_SERVICE")]
[assembly: UsesPermission("android.permission.FOREGROUND_SERVICE_DATA_SYNC")]

namespace Folio.Android.Platforms.Android;

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
        {
            CrashLogger.Log(args.ExceptionObject?.ToString() ?? "(no details)");
            // The same fault, in the file log the weekly upload carries (LOG-LOOP-PLAN.md §4.4).
            MauiLog.Current?.Write("Unhandled", Microsoft.Extensions.Logging.LogLevel.Critical, "Unhandled exception", args.ExceptionObject as Exception);
        };

        base.OnCreate();

        // Apply EF Core migrations once at process start (the process is created for WorkManager
        // jobs too), so the SQLite schema always exists before the worker or UI touches it.
        RunMigrations();
    }

    // Brings the on-device schema up to date. Records the outcome in SettingsStore so a failed update
    // surfaces on the Settings page rather than silently stalling every worker cycle. Also re-run
    // after a backup restore, since the restored database may predate a schema change.
    internal static void RunMigrations()
    {
        try
        {
            var factory = IPlatformApplication.Current!.Services
                .GetRequiredService<IDbContextFactory<FolioDbContext>>();
            using var db = factory.CreateDbContext();

            // Each reset generation drops the database once. Generation 1 was the pivot, which collapsed the
            // migration history into a single new baseline; generation 2 is P10's fresh start. A device holds
            // a database stamped with migration ids the current baseline cannot build on, so Migrate() would
            // try to create tables that already exist. The brief keeps no old data, so drop it once, then
            // migrate fresh — and record the generation, so this runs once per reset and never again.
            if (SettingsStore.SchemaResetIsDue)
            {
                db.Database.EnsureDeleted();

                // The rename moved the database file, so EnsureDeleted cannot reach the one the app
                // carried as Canonizer — it would sit on the device forever, holding books nothing can
                // open. Generation 2 is the only pass that will ever see it.
                LegacyDatabase.Remove(FileSystem.AppDataDirectory);

                SettingsStore.SchemaResetGeneration = SettingsStore.CurrentSchemaResetGeneration;
            }

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
        private const string LogTag = "Folio";
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
