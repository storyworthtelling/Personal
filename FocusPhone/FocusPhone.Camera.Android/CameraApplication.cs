using Android.App;
using Android.Runtime;
using FocusPhone.Ui;

namespace FocusPhone.Camera;

/// <summary>
/// Application entry point. The Camera head previously had none; it exists so
/// the crash reporter is installed before the camera stack starts in
/// <c>CameraActivity.OnCreate</c>. Icon attributes mirror the other heads.
/// </summary>
[Application(Icon = "@mipmap/appicon", RoundIcon = "@mipmap/appicon_round")]
public class CameraApplication : Application
{
    public CameraApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        CrashReporter.Install(this, "Camera");
    }
}
