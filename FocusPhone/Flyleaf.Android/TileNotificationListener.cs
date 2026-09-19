using Android.Service.Notification;
using Android.App;
using Android.Content;

namespace Flyleaf;

/// <summary>
/// Tracks which packages currently have active notifications, and each one's highest
/// notification urgency, so the home screen can tint the matching tiles' bookmark tabs by
/// priority rather than by the notifying app's own cloth color. Listener access is granted once
/// during provisioning via `adb shell cmd notification allow_listener`.
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

    /// <summary>
    /// Parallel to <see cref="ExtraPackages"/> — ExtraPriorities[i] is the normalized urgency
    /// (see <see cref="Urgency"/>) for ExtraPackages[i]. Kept as a second array rather than a
    /// serialized map so this stays as plain an Intent extra as the packages list already was.
    /// </summary>
    public const string ExtraPriorities = "priorities";

    public override void OnListenerConnected()
    {
        base.OnListenerConnected();
        BroadcastActivePackages();
    }

    public override void OnNotificationPosted(StatusBarNotification? sbn) => BroadcastActivePackages();

    public override void OnNotificationRemoved(StatusBarNotification? sbn) => BroadcastActivePackages();

    private void BroadcastActivePackages()
    {
        StatusBarNotification[] active;
        try
        {
            active = GetActiveNotifications() ?? [];
        }
        catch (Java.Lang.SecurityException)
        {
            // Listener access not granted yet (pre-provisioning); nothing to report.
            return;
        }

        // Highest urgency wins per package (a quiet download-progress notification shouldn't
        // mute a genuinely urgent one from the same app).
        var highest = new Dictionary<string, int>();
        foreach (var sbn in active)
        {
            var package = sbn.PackageName;
            if (package is null)
            {
                continue;
            }
            var urgency = Urgency(sbn.Notification);
            if (!highest.TryGetValue(package, out var current) || urgency > current)
            {
                highest[package] = urgency;
            }
        }

        var packages = highest.Keys.ToArray();
        var priorities = packages.Select(package => highest[package]).ToArray();

        var intent = new Intent(ActionNotificationsChanged);
        intent.SetPackage(PackageName);
        intent.PutExtra(ExtraPackages, packages);
        intent.PutExtra(ExtraPriorities, priorities);
        SendBroadcast(intent);
    }

    /// <summary>
    /// Normalizes one notification's urgency to a 0 (low) / 1 (default) / 2 (high) scale so the
    /// bookmark-hue mapping only has to know about three tiers rather than two overlapping
    /// Android priority systems. Prefers the notification channel's <see cref="NotificationImportance"/>
    /// (API 26+ — the modern, per-channel signal the owner actually controls in system settings)
    /// and falls back to the notification's own pre-channel <c>Priority</c> field when there is
    /// no channel to resolve (an older sender, or a channel deleted out from under a still-posted
    /// notification).
    /// </summary>
    private int Urgency(Notification? notification)
    {
        if (notification is null)
        {
            return 1;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(26) && notification.ChannelId is { Length: > 0 } channelId)
        {
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            var channel = manager?.GetNotificationChannel(channelId);
            if (channel is not null)
            {
                return channel.Importance switch
                {
                    NotificationImportance.Max or NotificationImportance.High => 2,
                    NotificationImportance.Low or NotificationImportance.Min or NotificationImportance.None => 0,
                    _ => 1,
                };
            }
        }

        return (NotificationPriority)notification.Priority switch
        {
            NotificationPriority.Max or NotificationPriority.High => 2,
            NotificationPriority.Low or NotificationPriority.Min => 0,
            _ => 1,
        };
    }
}
