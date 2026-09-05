using Android.App;
using Android.App.Admin;
using Android.Content;

namespace Flyleaf;

[BroadcastReceiver(
    Name = "com.storyworthtelling.focusphone.launcher.FocusDeviceAdminReceiver",
    Permission = "android.permission.BIND_DEVICE_ADMIN",
    Exported = true)]
[MetaData("android.app.device_admin", Resource = "@xml/device_admin")]
[IntentFilter(["android.app.action.DEVICE_ADMIN_ENABLED"])]
public class FocusDeviceAdminReceiver : DeviceAdminReceiver
{
    public override void OnEnabled(Context context, Intent intent)
    {
        base.OnEnabled(context, intent);
        // Provisioning step: once this app becomes device owner via
        // `adb shell dpm set-device-owner`, apply the lockdown policy.
        PolicyManager.ApplyCurrentPolicy(context);
    }
}
