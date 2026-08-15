using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace FocusPhone.Launcher;

/// <summary>Thrown when the device-owner silent install reports a failure (e.g. a signature mismatch).</summary>
public class ApkInstallException : Exception
{
    public int Status { get; }

    public ApkInstallException(int status, string? message)
        : base($"Install failed (status {status}): {(string.IsNullOrWhiteSpace(message) ? "no detail" : message)}")
    {
        Status = status;
    }
}

/// <summary>
/// Device-owner silent APK install. It needs neither adb nor unknown-sources, so
/// it is the sanctioned install path both for the update worker and for the
/// admin-mode "Install APK from Downloads" action — and it keeps working after
/// the cutover disables debugging (that is why the manual installer can replace
/// the adb sideload the owner uses during setup). Every session re-whitelists the
/// hard-restricted call-log/SMS permissions so a Communication app (re)install
/// keeps them; the flags are harmless for any other package that never requests
/// them.
///
/// Committing a PackageInstaller session is asynchronous: the real outcome
/// arrives later as a broadcast. This awaits that broadcast and throws on a
/// non-success status, so a failed install (the classic being a signature
/// mismatch against a differently-signed sideloaded build) surfaces as an error
/// instead of being silently swallowed by a commit that "succeeded".
/// </summary>
public static class ApkInstaller
{
    // A silent device-owner install completes in seconds; this only guards against
    // a lost broadcast wedging the worker forever.
    private static readonly TimeSpan ResultTimeout = TimeSpan.FromMinutes(2);

    public static async Task InstallAsync(Context context, Stream apk)
    {
        var installer = context.PackageManager!.PackageInstaller;
        var sessionParams = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        sessionParams.SetWhitelistedRestrictedPermissions(
            [
                Android.Manifest.Permission.ReadCallLog,
                Android.Manifest.Permission.ReadSms,
                Android.Manifest.Permission.WriteCallLog,
            ]);

        var sessionId = installer.CreateSession(sessionParams);
        using var session = installer.OpenSession(sessionId);
        using (var output = session.OpenWrite("focusphone.apk", 0, -1))
        {
            await apk.CopyToAsync(output);
            session.Fsync(output);
        }

        // A unique, package-internal action so this receiver only hears its own session's result.
        var action = $"{context.PackageName}.INSTALL_RESULT.{sessionId}";
        var completion = new TaskCompletionSource<(int Status, string? Message)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new InstallResultReceiver(completion);
        var filter = new IntentFilter(action);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            // Android 13+ requires an explicit export flag; this broadcast is internal.
            context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
        }
        else
        {
            context.RegisterReceiver(receiver, filter);
        }

        try
        {
            var statusIntent = new Intent(action).SetPackage(context.PackageName);
            var pending = PendingIntent.GetBroadcast(
                context, sessionId, statusIntent,
                PendingIntentFlags.Mutable | PendingIntentFlags.UpdateCurrent)!;
            session.Commit(pending.IntentSender);

            var finished = await Task.WhenAny(completion.Task, Task.Delay(ResultTimeout));
            if (finished != completion.Task)
            {
                throw new ApkInstallException(-1000, "Timed out waiting for the install to finish.");
            }

            var (status, message) = await completion.Task;
            if (status != (int)PackageInstallStatus.Success)
            {
                throw new ApkInstallException(status, message);
            }
        }
        finally
        {
            context.UnregisterReceiver(receiver);
        }
    }

    // Dynamically registered per session: hands the session's status back to the
    // awaiting InstallAsync. (Not the manifest-declared receiver — this one is
    // scoped to a single install so its result can be awaited.)
    private class InstallResultReceiver : BroadcastReceiver
    {
        private readonly TaskCompletionSource<(int, string?)> completion;

        public InstallResultReceiver(TaskCompletionSource<(int, string?)> completion) => this.completion = completion;

        public override void OnReceive(Context? context, Intent? intent)
        {
            var status = intent?.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure)
                ?? (int)PackageInstallStatus.Failure;
            var message = intent?.GetStringExtra(PackageInstaller.ExtraStatusMessage);
            completion.TrySetResult((status, message));
        }
    }
}
