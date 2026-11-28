using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using AndroidX.Core.App;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// Post's own notification for a newly-arrived text, and its buzz. The notification is what
/// puts the bookmark on Post's book on Flyleaf's shelf (it tracks each package's active
/// notifications); Google Messages, which holds the SMS role and is the one notifying the
/// system, is kept off the shelf, so without this a text arrives with nothing to say so.
/// The buzz is Post's own because the locked phone's disabled status bar turns off every
/// app's notification sound, vibration and heads-up card (the platform's disable mask
/// includes the alerts) — a channel set to vibrate is silenced with the rest. So the
/// channel is quiet, and Post vibrates the phone directly, as a notification-usage
/// vibration, which the platform still mutes when the ringer is silent or Do Not Disturb
/// holds notifications.
/// </summary>
internal static class SmsNotifications
{
    // The channel the old, vibrating notification used; deleted so its settings cannot linger.
    private const string RetiredChannelId = "messages";
    private const string ChannelId = "texts";
    private const string GroupKey = "com.storyworthtelling.focusphone.communication.MESSAGES";
    private const string Source = "Notifications";

    /// <summary>A text Post received itself (the SMS role's receiver, or a downloaded MMS).</summary>
    public static void NotifyIncoming(Context context, IncomingSms message)
    {
        var address = message.Address;
        var contactKey = address is null ? null : Codex.Domain.Timeline.ContactKeyNormalizer.NormalizeAny(address);
        var arriving = new Codex.Domain.Timeline.TimelineItem(
            Codex.Domain.Timeline.TimelineItemKind.Message, contactKey ?? string.Empty,
            DateTimeOffset.UtcNow, message.Body, Incoming: true, SourceId: string.Empty);
        if (IsSpam(context, arriving))
        {
            return;
        }

        var displayName = contactKey is null
            ? "Unknown"
            : ContactNameResolver.DisplayName(context, contactKey);
        Post(context, contactKey ?? address ?? "sms", displayName, message.Body ?? string.Empty, arriving);
    }

    /// <summary>A text Google Messages announced, decided by <see cref="TextAlerts"/>.</summary>
    public static void NotifyIncoming(Context context, TextAlert alert, Codex.Domain.Timeline.TimelineItem latest)
    {
        if (IsSpam(context, latest))
        {
            return;
        }
        Post(context, alert.ThreadKey, alert.Title, alert.Line, latest);
    }

    // A text the owner judged spam is still kept (the record is the phone's, not Post's to
    // drop), but it must not buzz: the notification is the one place it would surface
    // outside the Mailbox, which already hides it.
    private static bool IsSpam(Context context, Codex.Domain.Timeline.TimelineItem arriving)
    {
        try
        {
            return SpamStore.Load(context).Matches(arriving);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning(Source, "Could not read the spam list for an arriving text", e);
            return false;
        }
    }

    private static void Post(Context context, string threadKey, string title, string line, Codex.Domain.Timeline.TimelineItem arriving)
    {
        // The wrist's two short taps (P14 B8): told where the phone alerts, after the spam check, and only that.
        WristArrivals.Tell(context, arriving);

        var manager = NotificationManagerCompat.From(context)!;
        EnsureChannel(manager);

        if (manager.AreNotificationsEnabled())
        {
            var thread = new Intent(context, typeof(PostActivity));
            thread.PutExtra(PostActivity.ExtraOpenContactKey, threadKey);
            var pending = PendingIntent.GetActivity(context, threadKey.GetHashCode(), thread,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var notification = new NotificationCompat.Builder(context, ChannelId)
                .SetSmallIcon(Resource.Drawable.glyph_mailbox)!
                .SetContentTitle(title)!
                .SetContentText(line)!
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(line))!
                .SetPriority(NotificationCompat.PriorityHigh)!
                .SetCategory(NotificationCompat.CategoryMessage)!
                .SetAutoCancel(true)!
                .SetContentIntent(pending)!
                .SetGroup(GroupKey)!
                .Build();
            manager.Notify(threadKey.GetHashCode(), notification);
        }
        else
        {
            PostDiagnostics.LogWarning(Source, "A text arrived but Post may not post notifications; buzzing only");
        }

        Buzz(context);
    }

    private static void Buzz(Context context)
    {
        try
        {
            if (context.GetSystemService(Context.AudioService) is AudioManager audio
                && audio.RingerMode == RingerMode.Silent)
            {
                return;
            }
            if (context.GetSystemService(Context.VibratorManagerService) is not VibratorManager vibrators)
            {
                return;
            }
            var vibrator = vibrators.DefaultVibrator;
            if (!vibrator.HasVibrator)
            {
                return;
            }
            var effect = VibrationEffect.CreateWaveform(TextAlerts.BuzzPattern, -1);
            // Notification usage is one the platform lets a background app use, and the one
            // it mutes for the silent ringer and Do Not Disturb.
            var attributes = new VibrationAttributes.Builder()
                .SetUsage((int)VibrationAttributesUsageType.Notification)
                .Build();
            vibrator.Vibrate(effect, attributes);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning(Source, "Could not buzz for an arriving text", e);
        }
    }

    private static void EnsureChannel(NotificationManagerCompat manager)
    {
        manager.DeleteNotificationChannel(RetiredChannelId);
        var channel = new NotificationChannelCompat.Builder(ChannelId, NotificationManagerCompat.ImportanceHigh)
            .SetName("Texts")!
            .SetDescription("New texts arriving in the Mailbox")!
            .SetVibrationEnabled(false)!
            .SetSound(null, null)!
            .Build();
        manager.CreateNotificationChannel(channel);
    }
}
