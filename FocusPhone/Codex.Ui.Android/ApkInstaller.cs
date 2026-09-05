using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Codex.Ui;

/// <summary>Thrown when an install reports a failure (e.g. a signature mismatch).</summary>
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
/// Installs an APK, by whichever of the two routes the phone allows.
///
/// <b>Silently</b>, when the caller is the device owner: no adb and no
/// unknown-sources, which is the sanctioned path on the locked phone both for the
/// update worker and for the admin-mode "Install APK from Downloads" action, and
/// which keeps working after the cutover disables debugging. Every session
/// re-whitelists the hard-restricted call-log/SMS permissions so a Communication app
/// (re)install keeps them; the flags are harmless for any package that never asks
/// for them.
///
/// <b>Confirmed</b>, on an ordinary phone: the same session, but the system answers
/// the commit with a request to ask the person first. That is not a failure and must
/// not be treated as one — it is the only install route an app without device-owner
/// privilege has, and it is exactly the route the second phone in the household uses,
/// where the lockdown that makes silent installs possible deliberately does not
/// exist. The consent screen is launched and the same wait continues, so the caller
/// still learns the real outcome.
///
/// Committing a session is asynchronous either way: the real outcome arrives later as
/// a broadcast. This awaits that broadcast and throws on a non-success status, so a
/// failed install — the classic being a signature mismatch against a differently
/// signed sideloaded build — surfaces as an error rather than being swallowed by a
/// commit that "succeeded".
///
/// Shared rather than Flyleaf's, because both phones install the same suite: the
/// launcher does it for everyone on the locked one, and each app does it for itself
/// on the unrestricted one.
/// </summary>
public static class ApkInstaller
{
    // A silent install completes in seconds. A confirmed one waits for a person, who
    // may be nowhere near the phone — so the two get very different budgets, and
    // neither is allowed to wedge a worker forever.
    private static readonly TimeSpan SilentTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ConfirmedTimeout = TimeSpan.FromMinutes(10);

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
        var receiver = new InstallResultReceiver();
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
            var first = receiver.Arm();

            var statusIntent = new Intent(action).SetPackage(context.PackageName);
            var pending = PendingIntent.GetBroadcast(
                context, sessionId, statusIntent,
                PendingIntentFlags.Mutable | PendingIntentFlags.UpdateCurrent)!;
            session.Commit(pending.IntentSender);

            var signal = await Settle(first, SilentTimeout);

            // The system is asking to put the consent screen in front of somebody. An
            // app without device-owner privilege gets this every time, so treating it
            // as a failure would mean the second phone could never update at all.
            if (signal.Status == (int)PackageInstallStatus.PendingUserAction)
            {
                // Re-armed BEFORE the screen is shown: the same broadcast carries the
                // real answer, and arming afterwards would race a person who taps
                // Install quickly.
                var second = receiver.Arm();
                Confirm(context, signal.Confirm);
                signal = await Settle(second, ConfirmedTimeout);
            }

            if (signal.Status != (int)PackageInstallStatus.Success)
            {
                throw new ApkInstallException(signal.Status, signal.Message);
            }
        }
        finally
        {
            context.UnregisterReceiver(receiver);
        }
    }

    private static async Task<InstallSignal> Settle(
        TaskCompletionSource<InstallSignal> completion, TimeSpan budget)
    {
        var finished = await Task.WhenAny(completion.Task, Task.Delay(budget));
        if (finished != completion.Task)
        {
            throw new ApkInstallException(-1000, "Timed out waiting for the install to finish.");
        }

        return await completion.Task;
    }

    // The consent screen. NewTask because this is reached from a worker rather than
    // from an activity, and without it the launch is refused outright.
    private static void Confirm(Context context, Intent? confirm)
    {
        if (confirm is null)
        {
            throw new ApkInstallException(
                (int)PackageInstallStatus.Failure,
                "The installer asked for confirmation but supplied no screen to show.");
        }

        confirm.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(confirm);
    }

    /// <summary>One broadcast from the installer: what happened, and — when it is
    /// asking rather than answering — the screen to put in front of somebody.</summary>
    private sealed record InstallSignal(int Status, string? Message, Intent? Confirm);

    // Dynamically registered per session: hands each of the session's broadcasts back
    // to whichever wait is currently outstanding. A confirmed install settles twice —
    // once to ask, once with the real answer — so the completion source is replaced
    // between them rather than being a single shot.
    private sealed class InstallResultReceiver : BroadcastReceiver
    {
        private TaskCompletionSource<InstallSignal> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<InstallSignal> Arm() =>
            completion = new TaskCompletionSource<InstallSignal>(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void OnReceive(Context? context, Intent? intent)
        {
            var status = intent?.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure)
                ?? (int)PackageInstallStatus.Failure;
            var message = intent?.GetStringExtra(PackageInstaller.ExtraStatusMessage);
            var confirm = intent?.GetParcelableExtra(Intent.ExtraIntent) as Intent;
            completion.TrySetResult(new InstallSignal(status, message, confirm));
        }
    }
}
