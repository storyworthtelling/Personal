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
        // Install first so a fault in the startup work below is captured.
        CrashReporter.Install(this, "FocusPhone");
        packageWatcher = new PackageWatcher(this);
        packageWatcher.Start();
        // A lock-task session a dead process left behind is undone before anything else runs (D297).
        LockedSessionController.EndStaleAtStart(this);
        UpdateService.Schedule(this);
    }
}
