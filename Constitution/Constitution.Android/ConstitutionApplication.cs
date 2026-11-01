using Constitution.Domain;
using Android.App;
using Android.Runtime;
using Codex.Ui;
using Constitution.Body;
using Constitution.Budgets;
using Constitution.Domain.Diagnostics;
using Constitution.Services;
using Constitution.WorkoutsTab;

namespace Constitution;

/// <summary>
/// Application entry point for the log loop (LOG-LOOP-PLAN.md §3): installs the shared head diagnostics before
/// the first screen — the failure log and the four trails in <c>NoBackupFilesDir</c>, all sealing into the
/// outbox, the crash reporter, exit reasons, the tap watch and the weekly upload. The phone is left to the
/// vault's ownership, since Constitution runs on both adults' phones. The manifest's application attributes
/// stay on the assembly (MainActivity.cs), which names this class.
/// </summary>
[Register(Name)]
public class ConstitutionApplication : Application
{
    /// <summary>The registered Java name the assembly-level <c>[Application]</c> points at.</summary>
    public const string Name = ConstitutionIdentity.ApplicationId + ".ConstitutionApplication";

    public ConstitutionApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        HeadDiagnostics.Install(this, new HeadSetup(
            "Constitution", "constitution", ConstitutionKeyActions.SendLogsNow,
            ConstitutionGoogle.Create().GetAccessToken,
            () => Codex.Ui.Voice.VoiceGoogleAccount.IsSignedIn,
            Phone: null,
            Directory: NoBackupFilesDir?.AbsolutePath,
            ExtraLogs:
            [
                (BodyLog.FileName, BodyLog.MaxBytes),
                (ConstitutionVaultLog.FileName, ConstitutionVaultLog.MaxBytes),
                (WorkoutsLog.FileName, WorkoutsLog.MaxBytes),
                (BudgetTrail.FileName, BudgetTrail.MaxBytes),
            ]));
    }
}
