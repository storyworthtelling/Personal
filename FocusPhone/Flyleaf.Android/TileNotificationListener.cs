using Android.Service.Notification;
using Android.App;
using Android.Content;

namespace Flyleaf;

/// <summary>
/// Tracks which packages currently have active notifications so the home
/// screen can tint the matching tiles. Listener access is granted once during
/// provisioning via `adb shell cmd notification allow_listener`.
/// </summary>
[Service(
    Name = "com.storyworthtelling.focusphone.launcher.TileNotificationListener",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Exported = true)]
[IntentFilter(["android.service.notification.NotificationListenerService"])]
public class TileNotificationListener : NotificationListenerService
{
    public const string ActionNotificationsChanged =
        "com.storyworthtelling.focusphone.launcher.NOTIFICATIONS_CHANGED";
    public const string ExtraPackages = "packages";

    public override void OnListenerConnected()
    {
        base.OnListenerConnected();
        BroadcastActivePackages();
    }

    public override void OnNotificationPosted(StatusBarNotification? sbn) => BroadcastActivePackages();

    public override void OnNotificationRemoved(StatusBarNotification? sbn) => BroadcastActivePackages();

    private void BroadcastActivePackages()
    {
        string[] packages;
        try
        {
            packages = GetActiveNotifications()?
                .Select(notification => notification.PackageName!)
                .Distinct()
                .ToArray() ?? [];
        }
        catch (Java.Lang.SecurityException)
        {
            // Listener access not granted yet (pre-provisioning); nothing to report.
            return;
        }

        var intent = new Intent(ActionNotificationsChanged);
        intent.SetPackage(PackageName);
        intent.PutExtra(ExtraPackages, packages);
        SendBroadcast(intent);
    }
}
