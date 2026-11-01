using Codex.Ui.Voice;
using Codex.Domain.Diagnostics;
using Android.App;
using Android.Runtime;
using Codex.Ui;

namespace Flyleaf;

[Application(Icon = "@mipmap/appicon", RoundIcon = "@mipmap/appicon_round")]
public class FocusApplication : Application
{
    private PackageWatcher? packageWatcher;

    public FocusApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        // The log loop first (LOG-LOOP-PLAN.md), so a fault in the startup work below is captured: the
        // rolling log sealing into the outbox, every crash appended to it, the tap watch, the launch line,
        // every exit since the last launch, and the weekly upload. Flyleaf kept no file log before (G3).
        var account = new VoiceGoogleAccount();
        HeadDiagnostics.Install(this, new HeadSetup(
            "Flyleaf", "flyleaf", FlyleafKeyActions.SendLogsNow,
            account.GetAccessToken, () => VoiceGoogleAccount.IsSignedIn));
        packageWatcher = new PackageWatcher(this);
        packageWatcher.Start();
        // A lock-task session a dead process left behind is undone before anything else runs (D297).
        LockedSessionController.EndStaleAtStart(this);
        UpdateService.Schedule(this);
    }
}
