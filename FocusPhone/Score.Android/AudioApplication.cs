using Codex.Domain.Diagnostics;
using Android.App;
using Android.Runtime;
using Codex.Ui;

namespace Score;

/// <summary>
/// Application entry point. Score previously had none; it exists so
/// the crash reporter is installed before the media stack starts in
/// <c>ScoreActivity.OnCreate</c>. Icon attributes mirror the other heads.
/// </summary>
[Application(Icon = "@mipmap/appicon", RoundIcon = "@mipmap/appicon_round")]
public class AudioApplication : Application
{
    public AudioApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        // The log loop (LOG-LOOP-PLAN.md): the rolling log sealing into the outbox, every crash
        // appended to it, the tap watch, the launch line, every exit since the last launch, and the
        // weekly upload, over the music account's Drive token.
        HeadDiagnostics.Install(this, new HeadSetup(
            "Score", "score", ScoreKeyActions.SendLogsNow,
            new Score.Google.DeviceAccountTokenProvider().GetAccessToken,
            () => !string.IsNullOrEmpty(MusicSettingsStore.GoogleAccountEmail)));
    }
}
