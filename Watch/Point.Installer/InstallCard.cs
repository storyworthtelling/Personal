using Android.App;
using Android.Content;
using Watch.Domain;
using Watch.Domain.Updates;

namespace Point.Installer;

/// <summary>
/// The one prompt on the wrist (WD §6, §7, §11): a card with one sentence that opens the system's own install prompt,
/// on a channel that never buzzes or rings, since the watch's four buzzes each have one meaning and this is none of them.
/// </summary>
internal static class InstallCard
{
    /// <returns>False when notifications are off for the installer, so the card cannot be seen.</returns>
    public static bool Show(Context context, string package, WatchUpdateAction action, Intent confirm)
    {
        var manager = Manager(context);
        var channel = new NotificationChannel(WatchIdentity.InstallChannel, InstallPrompt.ChannelName, NotificationImportance.Low);
        channel.EnableVibration(false);
        channel.SetSound(null, null);
        manager.CreateNotificationChannel(channel);

        confirm.AddFlags(ActivityFlags.NewTask);
        var id = InstallPrompt.NotificationId(package);
        var open = PendingIntent.GetActivity(context, id, confirm, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var card = new Notification.Builder(context, WatchIdentity.InstallChannel)
            .SetSmallIcon(Android.Resource.Drawable.StatSysDownloadDone)
            .SetContentText(InstallPrompt.Sentence(package, action))
            .SetContentIntent(open)
            .SetAutoCancel(true)
            .Build();
        manager.Notify(id, card);
        return manager.AreNotificationsEnabled();
    }

    public static void Clear(Context context, string package) => Manager(context).Cancel(InstallPrompt.NotificationId(package));

    private static NotificationManager Manager(Context context) =>
        (NotificationManager)context.GetSystemService(Context.NotificationService)!;
}
