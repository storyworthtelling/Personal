using global::Android.App;
using global::Android.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Storyboarder.Infrastructure.Persistence;

// Declare permissions via assembly attributes so MAUI's manifest merger always includes them.
[assembly: UsesPermission(global::Android.Manifest.Permission.Internet)]
[assembly: UsesPermission(global::Android.Manifest.Permission.ReceiveBootCompleted)]
[assembly: UsesPermission(global::Android.Manifest.Permission.WakeLock)]
[assembly: UsesPermission("android.permission.POST_NOTIFICATIONS")]
[assembly: UsesPermission("android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS")]

namespace Storyboarder.Android.Platforms.Android;

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
                .GetRequiredService<IDbContextFactory<StoryboarderDbContext>>();
            using var db = factory.CreateDbContext();

            // Each reset generation drops the database once. Generation 1 was the pivot, which collapsed the
            // migration history into a single new baseline; generation 2 is P10's fresh start. A device holds
            // a database stamped with migration ids the current baseline cannot build on, so Migrate() would
            // try to create tables that already exist. The brief keeps no old data, so drop it once, then
            // migrate fresh — and record the generation, so this runs once per reset and never again.
            if (SettingsStore.SchemaResetIsDue)
            {
                db.Database.EnsureDeleted();
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
        private const string LogTag = "Storyboarder";
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
