using Android.Content;
using Android.OS;

namespace Point.Installer;

/// <summary>Where the installer's work runs, and the one place an escaped failure is caught and logged.</summary>
internal static class Background
{
    /// <summary>
    /// Runs the work and waits for it when on Play services' own listener thread, which keeps the listener bound until
    /// the work is done; on the main thread it is handed to the pool instead, so the watch never stalls.
    /// </summary>
    public static void Run(Func<Task> work)
    {
        var running = Logged(work);
        if (Looper.MyLooper() != Looper.MainLooper)
        {
            running.GetAwaiter().GetResult();
        }
    }

    /// <summary>A receiver's work, finishing its <see cref="BroadcastReceiver.GoAsync"/> once done.</summary>
    public static void Finish(BroadcastReceiver.PendingResult? pending, Func<Task> work) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await Logged(work);
            }
            finally
            {
                pending?.Finish();
            }
        });

    private static async Task Logged(Func<Task> work)
    {
        try
        {
            await Task.Run(work);
        }
        catch (Exception error)
        {
            // The thread's boundary: a failure nothing nearer answered would otherwise vanish with its task. Its type and
            // frames are logged, never its message, which can carry a path or a payload.
            InstallerLog.Warn($"UPDATE work stopped: {error.GetType().FullName}{System.Environment.NewLine}{error.StackTrace}");
        }
    }
}

/// <summary>The installer's log, in logcat under one tag: outcomes and contract lines only (plan §5).</summary>
internal static class InstallerLog
{
    private const string Tag = "Point";

    public static void Info(string line) => Android.Util.Log.Info(Tag, line);

    public static void Warn(string line) => Android.Util.Log.Warn(Tag, line);
}
