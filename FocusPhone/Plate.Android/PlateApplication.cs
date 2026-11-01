using Codex.Ui.Voice;
using Codex.Domain.Diagnostics;
using Android.App;
using Android.Runtime;
using Codex.Ui;

namespace Plate;

/// <summary>
/// Application entry point. The Camera head previously had none; it exists so
/// the crash reporter is installed before the camera stack starts in
/// <c>PlateActivity.OnCreate</c>. Icon attributes mirror the other heads.
/// </summary>
[Application(Icon = "@mipmap/appicon", RoundIcon = "@mipmap/appicon_round")]
public class PlateApplication : Application
{
    public PlateApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        // The log loop (LOG-LOOP-PLAN.md): the rolling log sealing into the outbox, every crash appended
        // to it, the tap watch, the launch line, every exit since the last launch, and the weekly upload.
        // Plate kept no file log before (G3).
        HeadDiagnostics.Install(this, new HeadSetup(
            "Plate", "plate", PlateKeyActions.SendLogsNow,
            new VoiceGoogleAccount().GetAccessToken, () => VoiceGoogleAccount.IsSignedIn));
    }
}
