using Android.App;
using Android.Content;
using AndroidX.Core.App;

namespace Post;

/// <summary>
/// Posts the system notification for a newly-arrived text — the buzz and the heads-up, not
/// just the Mailbox row. Before Post held the SMS role, Google Messages received the
/// broadcast and notified on its own; Android does not do that on the role holder's behalf,
/// so once Post became the default SMS app that notification simply stopped happening.
/// This is also what makes the text bookmark tab appear on Flyleaf's bookshelf — that tab
/// tracks each package's currently-active notifications, and a text that never posts one is
/// a text nobody outside Post's own Mailbox knows arrived.
/// </summary>
internal static class SmsNotifications
{
    private const string ChannelId = "messages";
    private const string GroupKey = "com.storyworthtelling.focusphone.communication.MESSAGES";

    public static void NotifyIncoming(Context context, IncomingSms message)
    {
        var manager = NotificationManagerCompat.From(context)!;
        EnsureChannel(manager);
        if (!manager.AreNotificationsEnabled())
        {
            return; // POST_NOTIFICATIONS not granted yet; nothing to show.
        }

        var address = message.Address;
        var contactKey = address is null ? null : Codex.Domain.Timeline.ContactKeyNormalizer.NormalizeAny(address);

        // A text the owner judged spam is still written to the provider (the record is the
        // phone's, not Post's to drop), but it must not buzz: the notification is the one
        // place it would surface outside the Mailbox, which already hides it.
        var arriving = new Codex.Domain.Timeline.TimelineItem(
            Codex.Domain.Timeline.TimelineItemKind.Message, contactKey ?? string.Empty,
            DateTimeOffset.UtcNow, message.Body, Incoming: true, SourceId: string.Empty);
        if (SpamStore.Load(context).Matches(arriving))
        {
            return;
        }

        var displayName = contactKey is null
            ? "Unknown"
            : ContactNameResolver.DisplayName(context, contactKey);

        var key = contactKey ?? address ?? "sms";
        var thread = new Intent(context, typeof(PostActivity));
        thread.PutExtra(PostActivity.ExtraOpenContactKey, key);
        var pending = PendingIntent.GetActivity(context, key.GetHashCode(), thread,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Drawable.area_mailbox)!
            .SetContentTitle(displayName)!
            .SetContentText(message.Body)!
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(message.Body))!
            .SetPriority(NotificationCompat.PriorityHigh)!
            .SetCategory(NotificationCompat.CategoryMessage)!
            .SetAutoCancel(true)!
            .SetContentIntent(pending)!
            .SetGroup(GroupKey)!
            .Build();

        manager.Notify(key.GetHashCode(), notification);
    }

    private static void EnsureChannel(NotificationManagerCompat manager)
    {
        var channel = new NotificationChannelCompat.Builder(ChannelId, NotificationManagerCompat.ImportanceHigh)
            .SetName("Messages")!
            .SetDescription("New texts arriving in the Mailbox")!
            .SetVibrationEnabled(true)!
            .Build();
        manager.CreateNotificationChannel(channel);
    }
}
