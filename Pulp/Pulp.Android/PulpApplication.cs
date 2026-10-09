using Android.App;
using Android.Runtime;
using Codex.Ui;

namespace Pulp;

/// <summary>
/// Application entry point: names the launcher icon IconStudio generates from the den painting
/// (D200, D267), and installs the crash reporter before the first screen starts. Icon attributes
/// mirror the other heads (guarded by <c>LauncherIconTests</c>).
/// </summary>
[Application(Icon = "@mipmap/appicon", RoundIcon = "@mipmap/appicon_round", Label = "Pulp")]
public class PulpApplication : Application
{
    public PulpApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        CrashReporter.Install(this, "Pulp");
    }
}
