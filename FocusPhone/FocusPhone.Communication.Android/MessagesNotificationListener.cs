using Android.App;
using Android.Service.Notification;
using FocusPhone.Domain.Timeline;

namespace FocusPhone.Communication;

/// <summary>
/// Tier-2 messaging bridge: captures incoming texts from Google Messages
/// notifications (MessagingStyle) into the timeline cache. Listener access is
/// granted during provisioning. Tier 1 (the embedded Messages-for-Web bridge)
/// replaces this as the primary source once gmbridge lands; tier 3 (deep link
/// into Google Messages) always remains for replying.
/// </summary>
[Service(
    Name = "com.storyworthtelling.focusphone.communication.MessageBridgeListener",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Exported = true)]
[IntentFilter(["android.service.notification.NotificationListenerService"])]
public class MessagesNotificationListener : NotificationListenerService
{
    private const string GoogleMessagesPackage = "com.google.android.apps.messaging";

    public override void OnNotificationPosted(StatusBarNotification? sbn)
    {
        if (sbn?.PackageName != GoogleMessagesPackage || sbn.Notification is null)
        {
            return;
        }

        var style = AndroidX.Core.App.NotificationCompat.MessagingStyle
            .ExtractMessagingStyleFromNotification(sbn.Notification);
        if (style is null)
        {
            return;
        }

        var items = new List<TimelineItem>();
        foreach (var message in style.Messages)
        {
            var sender = message.Person?.Name?.ToString() ?? style.ConversationTitle?.ToString() ?? "";
            if (string.IsNullOrEmpty(sender))
            {
                continue;
            }
            items.Add(new TimelineItem(
                TimelineItemKind.Message,
                ContactKey(sender),
                DateTimeOffset.FromUnixTimeMilliseconds(message.Timestamp),
                message.Text?.ToString(),
                Incoming: true,
                SourceId: $"{ContactKey(sender)}:{message.Timestamp}"));
        }

        if (items.Count > 0)
        {
            try
            {
                using var cache = TimelineDatabase.Open(this);
                cache.Cache.Upsert(items);
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn("FocusPhoneComm", $"Could not cache messages: {e}");
            }
        }
    }

    /// <summary>
    /// Notifications carry display names, not addresses; when the name looks
    /// like a phone number it is normalized, otherwise the name itself keys
    /// the thread (merged with contacts at render time).
    /// </summary>
    private static string ContactKey(string sender) =>
        sender.Any(char.IsAsciiDigit)
            ? ContactKeyNormalizer.NormalizePhone(sender)
            : sender.Trim().ToLowerInvariant();
}
