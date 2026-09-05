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
        CrashReporter.Install(this, "Plate");
    }
}
