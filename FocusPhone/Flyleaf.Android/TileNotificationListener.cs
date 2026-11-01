using Codex.Ui;
using Android.Service.Notification;
using Android.App;
using Android.Content;
using Codex.Domain.Apps;
using Codex.Domain.Calls;

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

    /// <summary>
    /// Parallel to <see cref="ExtraPackages"/> — the role word a package's notifications carried
    /// (<see cref="NotificationBookmark.RoleExtraKey"/>), or "" for none. Words only, never a count
    /// (P13 C-10.2.2).
    /// </summary>
    public const string ExtraRoles = "roles";

    public override void OnListenerConnected()
    {
        base.OnListenerConnected();
        BroadcastActivePackages();
        sessions ??= new PlaybackSessionWatcher(this, new ComponentName(this, Class));
        sessions.Start();
    }

    public override void OnListenerDisconnected()
    {
        sessions?.Stop();
        base.OnListenerDisconnected();
    }

    // Relays the playback sessions of the apps Pulp named (P12, finding 2.3); see PlaybackSessionWatcher.
    private PlaybackSessionWatcher? sessions;

    // One per bound listener: a rebind starts with no memory of what was ringing, which at worst
    // opens a still-ringing call screen once more.
    private readonly IncomingCallSurfacer calls = new();

    public override void OnNotificationPosted(StatusBarNotification? sbn)
    {
        SurfaceIfRinging(sbn);
        BroadcastActivePackages();
    }

    public override void OnNotificationRemoved(StatusBarNotification? sbn)
    {
        if (sbn?.Key is { } key)
        {
            calls.Removed(key);
        }
        BroadcastActivePackages();
    }

    // The lockdown's disabled status bar also disables notification alerts, so a ringing call's
    // heads-up card never appears (see CallScreen). Open the call screen the dialer asked for.
    private void SurfaceIfRinging(StatusBarNotification? sbn)
    {
        if (sbn?.Key is not { } key || sbn.Notification is not { } notification)
        {
            return;
        }
        var kind = CallScreen.Classify(notification);
        if (kind == CallNoticeKind.None)
        {
            return;
        }
        if (!calls.ShouldSurface(key, kind))
        {
            return;
        }
        if (CallScreen.Target(notification, kind) is { } target)
        {
            // The record a call that rang unseen would leave (LOG-LOOP-PLAN.md §4.5).
            HeadDiagnostics.Current?.Info("Call", CallNotice.Line(kind, surfaced: true, opened: CallScreen.Open(target)));
        }
    }

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

        CallScreen.UpdateCurrent(active);

        // Highest urgency wins per package (a quiet download-progress notification shouldn't
        // mute a genuinely urgent one from the same app).
        var highest = new Dictionary<string, int>();
        var roleWords = new Dictionary<string, List<string?>>();
        foreach (var sbn in active)
        {
            var package = sbn.PackageName;
            if (package is null)
            {
                continue;
            }
            var urgency = Urgency(sbn.Notification);
            if (!roleWords.TryGetValue(package, out var words))
            {
                roleWords[package] = words = [];
            }
            words.Add(sbn.Notification?.Extras?.GetString(NotificationBookmark.RoleExtraKey));
            if (!highest.TryGetValue(package, out var current) || urgency > current)
            {
                highest[package] = urgency;
            }
        }

        var packages = highest.Keys.ToArray();
        var priorities = packages.Select(package => highest[package]).ToArray();

        var roles = packages.Select(package => NotificationBookmark.Strongest(package, roleWords[package]) ?? "").ToArray();

        var intent = new Intent(ActionNotificationsChanged);
        intent.SetPackage(PackageName);
        intent.PutExtra(ExtraPackages, packages);
        intent.PutExtra(ExtraPriorities, priorities);
        intent.PutExtra(ExtraRoles, roles);
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
