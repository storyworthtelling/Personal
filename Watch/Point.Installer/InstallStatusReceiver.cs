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
    // No session in the intent: a status from an installer before the fix round.
    private const int NoSession = -1;

    // The status intent's extras are WatchIdentity's frozen names, and the action goes as its frozen word: the intent can
    // outlive this process, and this installer's own update (release A fix round, finding 5).
    internal static PendingIntent For(Context context, int sessionId, UpdateApkHeader header, WatchUpdateDecision decision)
    {
        var intent = new Intent(context, typeof(InstallStatusReceiver))
            .PutExtra(WatchIdentity.StatusPackageExtra, header.Package)
            .PutExtra(WatchIdentity.StatusVersionCodeExtra, header.VersionCode)
            .PutExtra(WatchIdentity.StatusActionExtra, WatchUpdateActions.Word(decision.Action))
            .PutExtra(WatchIdentity.StatusSilenceExtra, decision.RequestsSilence)
            .PutExtra(WatchIdentity.StatusSessionExtra, sessionId);
        // Mutable so the platform can add the status; explicit, so nothing else can be sent through it.
        return PendingIntent.GetBroadcast(context, sessionId, intent, PendingIntentFlags.Mutable | PendingIntentFlags.UpdateCurrent)!;
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.GetStringExtra(WatchIdentity.StatusPackageExtra) is not { } package)
        {
            InstallerLog.Warn("UPDATE status: a session status with no package, ignored");
            return;
        }

        var versionCode = intent.GetLongExtra(WatchIdentity.StatusVersionCodeExtra, 0);
        var action = WatchUpdateActions.FromWord(intent.GetStringExtra(WatchIdentity.StatusActionExtra)) ?? WatchUpdateAction.FirstInstall;
        var silence = intent.GetBooleanExtra(WatchIdentity.StatusSilenceExtra, false);
        var session = SessionOf(intent);
        var status = StatusOf(intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure));
        var confirm = intent.GetParcelableExtra(Intent.ExtraIntent, Java.Lang.Class.FromType(typeof(Intent))) as Intent;

        var pending = GoAsync();
        Background.Finish(pending, () => SettleAsync(context, package, versionCode, action, silence, session, status, confirm));
    }

    private static async Task SettleAsync(Context context, string package, long versionCode, WatchUpdateAction action, bool silence, int? session, InstallerStatus status, Intent? confirm)
    {
        // A status is the app's outcome only if it is for the app's current session: an abandoned session's abort is not a
        // decline, and must neither be recorded over the build waiting now nor clear that build's card.
        switch (InstallSessions.Judge(SessionStore.Read(context), package, session, status, DateTimeOffset.UtcNow))
        {
            case SessionVerdict.Ignore:
                InstallerLog.Info(UpdateLines.Ignored(package, versionCode));
                return;

            case SessionVerdict.LetGo:
                // Nobody turned it down: the record stays waiting, and the phone offers the build again after the same
                // three days (WatchSendPlan.WaitingOffer). The card goes, since there is nothing left behind it to tap.
                InstallCard.Clear(context, package);
                End(context, package, session);
                InstallerLog.Info(UpdateLines.LetGo(package, versionCode));
                await Reporter.SendAsync(context, null);
                return;
        }

        var outcome = WatchInstallOutcomes.FromStatus(status, InstallResults.WasWaiting(ResultStore.Read(context), package, versionCode));
        if (outcome == WatchInstallOutcome.Waiting)
        {
            if (confirm is null)
            {
                // Asked to ask, with nothing to show: nobody can say yes, so it is a failure, sent again next run.
                outcome = WatchInstallOutcome.Failed;
                status = InstallerStatus.Failure;
                End(context, package, session);
            }
            else if (!InstallCard.Show(context, package, action, confirm))
            {
                InstallerLog.Warn(UpdateLines.CannotAsk(package, versionCode));
            }
        }
        else
        {
            InstallCard.Clear(context, package);
            End(context, package, session);
        }

        InstallerLog.Info(UpdateLines.Install(package, versionCode, outcome, status, silence));
        await Outcomes.RecordAsync(context, new WatchInstallResult(package, versionCode, outcome, outcome == WatchInstallOutcome.Failed ? status : null), logged: true);
    }

    // Ours first; the platform puts the session's id on every status it sends, which covers an intent from before ours.
    private static int? SessionOf(Intent intent)
    {
        var session = intent.GetIntExtra(WatchIdentity.StatusSessionExtra, NoSession);
        if (session == NoSession)
        {
            session = intent.GetIntExtra(PackageInstaller.ExtraSessionId, NoSession);
        }
        return session == NoSession ? null : session;
    }

    private static void End(Context context, string package, int? session)
    {
        if (session is { } id)
        {
            SessionStore.Ended(context, package, id);
        }
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
