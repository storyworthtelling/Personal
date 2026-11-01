using Storyworthtelling.Diagnostics;
using Codex.Domain.Diagnostics;
using Constitution.Domain.Diagnostics;
using Codex.Ui;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Constitution.Domain.Body;
using Constitution.Domain.Health;

namespace Constitution.Health;

/// <summary>
/// Asks Health Connect for one signal's read permissions through the platform's own permission request. On
/// Android 14 and above the health permissions are ordinary runtime permissions, so
/// <see cref="Activity.RequestPermissions(string[], int)"/> shows Health Connect's dialog; the shell forwards the
/// answer from <c>OnRequestPermissionsResult</c> to <see cref="OnResult"/>. Only the signal's read permissions are
/// asked for: there is no write anywhere in this app (the manifest has none), and nothing is asked up front.
///
/// <para>One request at a time. A second call while one is open returns false at once rather than stacking dialogs.
/// If the platform has stopped showing the dialog (the owner declined it twice), the request returns not granted
/// without a dialog and <see cref="OpenHealthConnectSettings"/> is the way on.</para>
/// </summary>
public sealed class ActivityHealthPermissionRequester(Activity activity) : IHealthPermissionRequester
{
    /// <summary>Distinct from the Google account picker's request code (9201).</summary>
    public const int RequestCode = 7311;

    private const string ManageHealthPermissionsAction = "android.health.connect.action.MANAGE_HEALTH_PERMISSIONS";

    private TaskCompletionSource<bool>? pending;
    private BodySignal pendingSignal;
    private KeyAction? asking;

    public Task<bool> RequestAsync(BodySignal signal, CancellationToken cancellationToken)
    {
        if (pending is { Task.IsCompleted: false })
        {
            return Task.FromResult(false);
        }

        var permissions = HealthSignalPermissions.For(signal).ToArray();
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = source;
        pendingSignal = signal;
        asking = TapWatch.Action(ConstitutionKeyActions.HealthConnectPermission, TimeSpan.FromMinutes(5));
        cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
        activity.RequestPermissions(permissions, RequestCode);
        return source.Task;
    }

    /// <summary>
    /// Takes the platform's answer. Returns true when the code was this requester's. The signal counts as allowed
    /// when its primary permission was granted, which is the one the reader checks (<c>HealthConnectSource.IsGranted</c>).
    /// </summary>
    public bool OnResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        if (requestCode != RequestCode)
        {
            return false;
        }

        var primary = HealthSignalPermissions.Primary(pendingSignal);
        var index = Array.IndexOf(permissions, primary);
        var granted = index >= 0 && index < grantResults.Length && grantResults[index] == Permission.Granted;
        // §4.5: the platform's answer, by signal and outcome; an event, never a reading (O7).
        new Constitution.Body.BodyLog(activity).Outcome(
            $"health permission {pendingSignal.ToString().ToLowerInvariant()}: {(granted ? "granted" : "not granted")}");
        asking?.Succeeded();
        asking = null;
        pending?.TrySetResult(granted);
        return true;
    }

    /// <summary>
    /// Opens Health Connect's own page for this app's permissions, for when the dialog will no longer appear.
    /// Returns false, and says why to the Body trail by the exception's type only, when the platform has no such page.
    /// </summary>
    public bool OpenHealthConnectSettings(IBodyLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        try
        {
            var intent = new Intent(ManageHealthPermissionsAction).PutExtra(Intent.ExtraPackageName, activity.PackageName);
            activity.StartActivity(intent);
            return true;
        }
        catch (ActivityNotFoundException e)
        {
            log.Outcome($"health settings page not opened: {e.GetType().Name}");
            return false;
        }
    }
}
