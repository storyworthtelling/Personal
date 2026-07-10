using Android.App;
using Android.Runtime;
using FocusPhone.Ui;

namespace FocusPhone.Launcher;

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
        // Install first so a fault in the startup work below is captured.
        CrashReporter.Install(this, "FocusPhone");
        packageWatcher = new PackageWatcher(this);
        packageWatcher.Start();
        UpdateService.Schedule(this);
    }
}
