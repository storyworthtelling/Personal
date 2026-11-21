using Android.App;
using Android.Content;
using Android.Content.PM;
using Watch.Domain;
using Watch.Domain.Updates;

namespace Point.Installer;

/// <summary>
/// Hears how each install session went. A first install, or an update the platform will not take silently, comes back
/// first as "pending user action" with the system's prompt to show: the installer has no screen, and Android does not
/// let a background app put an activity in front of anyone, so the prompt waits behind one silent card on the wrist
/// (<see cref="InstallCard"/>). The same session's real answer arrives here afterwards.
/// </summary>
[BroadcastReceiver(Name = WatchIdentity.InstallStatusReceiverName, Exported = false)]
public sealed class InstallStatusReceiver : BroadcastReceiver
{
    // Extras of the installer's own status intent; it lives only as long as the session that carries it.
    private const string PackageExtra = "package";
    private const string VersionCodeExtra = "versionCode";
    private const string ActionExtra = "action";
    private const string SilenceExtra = "silence";

    internal static PendingIntent For(Context context, int sessionId, UpdateApkHeader header, WatchUpdateDecision decision)
    {
        var intent = new Intent(context, typeof(InstallStatusReceiver))
            .PutExtra(PackageExtra, header.Package)
            .PutExtra(VersionCodeExtra, header.VersionCode)
            .PutExtra(ActionExtra, decision.Action.ToString())
            .PutExtra(SilenceExtra, decision.RequestsSilence);
        // Mutable so the platform can add the status; explicit, so nothing else can be sent through it.
        return PendingIntent.GetBroadcast(context, sessionId, intent, PendingIntentFlags.Mutable | PendingIntentFlags.UpdateCurrent)!;
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.GetStringExtra(PackageExtra) is not { } package)
        {
            InstallerLog.Warn("UPDATE status: a session status with no package, ignored");
            return;
        }

        var versionCode = intent.GetLongExtra(VersionCodeExtra, 0);
        var action = Enum.TryParse<WatchUpdateAction>(intent.GetStringExtra(ActionExtra), out var asked) ? asked : WatchUpdateAction.FirstInstall;
        var silence = intent.GetBooleanExtra(SilenceExtra, false);
        var status = StatusOf(intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure));
        var confirm = intent.GetParcelableExtra(Intent.ExtraIntent, Java.Lang.Class.FromType(typeof(Intent))) as Intent;

        var pending = GoAsync();
        Background.Finish(pending, () => SettleAsync(context, package, versionCode, action, silence, status, confirm));
    }

    private static async Task SettleAsync(Context context, string package, long versionCode, WatchUpdateAction action, bool silence, InstallerStatus status, Intent? confirm)
    {
        var outcome = WatchInstallOutcomes.FromStatus(status, InstallResults.WasWaiting(ResultStore.Read(context), package, versionCode));
        if (outcome == WatchInstallOutcome.Waiting)
        {
            if (confirm is null)
            {
                // Asked to ask, with nothing to show: nobody can say yes, so it is a failure, sent again next run.
                outcome = WatchInstallOutcome.Failed;
                status = InstallerStatus.Failure;
            }
            else if (!InstallCard.Show(context, package, action, confirm))
            {
                InstallerLog.Warn(UpdateLines.CannotAsk(package, versionCode));
            }
        }
        else
        {
            InstallCard.Clear(context, package);
        }

        InstallerLog.Info(UpdateLines.Install(package, versionCode, outcome, status, silence));
        await Outcomes.RecordAsync(context, new WatchInstallResult(package, versionCode, outcome, outcome == WatchInstallOutcome.Failed ? status : null), logged: true);
    }

    // The platform's status by name, so no platform number is copied into the domain.
    private static InstallerStatus StatusOf(int status) => (PackageInstallStatus)status switch
    {
        PackageInstallStatus.PendingUserAction => InstallerStatus.PendingUserAction,
        PackageInstallStatus.Success => InstallerStatus.Success,
        PackageInstallStatus.Failure => InstallerStatus.Failure,
        PackageInstallStatus.FailureBlocked => InstallerStatus.Blocked,
        PackageInstallStatus.FailureAborted => InstallerStatus.Aborted,
        PackageInstallStatus.FailureInvalid => InstallerStatus.Invalid,
        PackageInstallStatus.FailureConflict => InstallerStatus.Conflict,
        PackageInstallStatus.FailureStorage => InstallerStatus.Storage,
        PackageInstallStatus.FailureIncompatible => InstallerStatus.Incompatible,
        PackageInstallStatus.FailureTimeout => InstallerStatus.Timeout,
        _ => InstallerStatus.Unrecognised,
    };
}

/// <summary>Tells the phone what the watch has once the installer itself has been replaced: a self-update ends the
/// process before its own session's status can arrive.</summary>
[BroadcastReceiver(Name = WatchIdentity.ReplacedReceiverName, Exported = false)]
[IntentFilter([Intent.ActionMyPackageReplaced])]
public sealed class ReplacedReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }
        Background.Finish(GoAsync(), () => Reporter.SendAsync(context, null));
    }
}
