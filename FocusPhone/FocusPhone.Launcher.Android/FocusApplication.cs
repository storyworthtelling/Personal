using Android.App;
using Android.Runtime;

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
        packageWatcher = new PackageWatcher(this);
        packageWatcher.Start();
        UpdateService.Schedule(this);
    }
}
