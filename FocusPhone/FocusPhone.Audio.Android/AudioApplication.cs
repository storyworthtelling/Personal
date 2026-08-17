using Android.App;
using Android.Runtime;
using Codex.Ui;

namespace FocusPhone.Audio;

/// <summary>
/// Application entry point. The Audio head previously had none; it exists so
/// the crash reporter is installed before the media stack starts in
/// <c>AudioActivity.OnCreate</c>. Icon attributes mirror the other heads.
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
        CrashReporter.Install(this, "Audio");

        // The rolling, reviewable log of non-fatal playback/queue failures (and a
        // copy of each crash), on top of the one-shot CrashReporter above.
        AudioDiagnostics.Install(this);
    }
}
