using Android.App;
using Android.App.Admin;
using Android.Content;

namespace WatchSpike;

/// <summary>
/// The device-admin receiver the device-owner spike names:
/// <c>adb shell dpm set-device-owner com.storyworthtelling.watchspike/.SpikeAdmin</c>.
/// It asks for no policies of its own; being device owner is the whole question.
/// </summary>
[BroadcastReceiver(Name = "com.storyworthtelling.watchspike.SpikeAdmin", Exported = true, Permission = "android.permission.BIND_DEVICE_ADMIN")]
[MetaData("android.app.device_admin", Resource = "@xml/device_admin")]
[IntentFilter(["android.app.action.DEVICE_ADMIN_ENABLED"])]
public class SpikeAdmin : DeviceAdminReceiver
{
}

/// <summary>Receives the result of the silent reinstall test and records its status code.</summary>
[BroadcastReceiver(Name = "com.storyworthtelling.watchspike.InstallResultReceiver", Exported = false)]
public class InstallResultReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        var status = intent.GetIntExtra(Android.Content.PM.PackageInstaller.ExtraStatus, -999);
        var meaning = status switch
        {
            0 => "installed with no prompt",
            -1 => "the watch wants to ask first (a prompt would appear)",
            _ => $"failed, status {status}",
        };
        if (status == -1)
        {
            // Pending user action: the watch is not letting it install silently. Show the prompt so the
            // owner can see it happen, and record that a prompt was needed.
            var confirm = intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
            if (confirm is not null)
            {
                confirm.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(confirm);
            }
        }
        SpikeLog.Note(context, $"Silent reinstall: {meaning}.");
    }
}
