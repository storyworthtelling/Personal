using Android.App;
using Android.Service.Notification;
using FocusPhone.Domain.Timeline;

namespace FocusPhone.Communication;

/// <summary>
/// Tier-2 messaging bridge: captures incoming texts from Google Messages
/// notifications (MessagingStyle) and voicemails (with transcription) from
/// Google Phone notifications into the timeline cache. Listener access is
/// granted during provisioning. For texts, the SMS provider is the
/// authoritative history and near-duplicates are collapsed at merge time, so
/// this listener's lasting job is RCS (which never reaches the provider) and
/// voicemail transcriptions. Tier 1 (the embedded Messages-for-Web bridge)
/// replaces the text capture as the primary source once gmbridge lands; tier
/// 3 (deep link into Google Messages) always remains for replying.
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
        if (sbn?.Notification is null)
        {
            return;
        }
        if (sbn.PackageName == VoicemailNotificationParser.DialerPackage)
        {
            CaptureVoicemail(sbn);
        }
        else if (sbn.PackageName == GoogleMessagesPackage)
        {
            CaptureMessages(sbn);
        }
    }

    private void CaptureMessages(StatusBarNotification sbn)
    {
        var style = AndroidX.Core.App.NotificationCompat.MessagingStyle
            .ExtractMessagingStyleFromNotification(sbn.Notification!);
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
            var contactKey = ContactKey(sender);
            items.Add(new TimelineItem(
                TimelineItemKind.Message,
                contactKey,
                DateTimeOffset.FromUnixTimeMilliseconds(message.Timestamp),
                message.Text?.ToString(),
                Incoming: true,
                SourceId: $"{contactKey}:{message.Timestamp}"));
        }
        Cache(items);
    }

    private void CaptureVoicemail(StatusBarNotification sbn)
    {
        var extras = sbn.Notification!.Extras;
        var item = VoicemailNotificationParser.TryParse(
            sbn.PackageName,
            sbn.Notification.ChannelId,
            extras?.GetCharSequence(Notification.ExtraTitle)?.ToString(),
            extras?.GetCharSequence(Notification.ExtraText)?.ToString()
                ?? extras?.GetCharSequence(Notification.ExtraBigText)?.ToString(),
            sbn.Notification.When);
        if (item is null)
        {
            return;
        }
        // The parser keys by whatever the title carried; when that is a display
        // name, re-key to the contact's number so the voicemail lands in the
        // same thread as the calls. The SourceId keeps the original key so the
        // id stays stable however contacts change later.
        if (!item.ContactKey.Contains('@') && !item.ContactKey.All(char.IsAsciiDigit))
        {
            var number = ContactNameResolver.PhoneForDisplayName(this, item.ContactKey);
            if (number is not null)
            {
                item = item with { ContactKey = number };
            }
        }
        Cache([item]);
    }

    private void Cache(List<TimelineItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }
        try
        {
            using var cache = TimelineDatabase.Open(this);
            cache.Cache.Upsert(items);
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Could not cache notification items: {e}");
        }
    }

    /// <summary>
    /// Notifications carry display names, not addresses; a name is first
    /// reverse-resolved to the contact's number so the message threads with
    /// calls and provider texts, else the name itself keys the thread
    /// (merged with contacts at render time).
    /// </summary>
    private string ContactKey(string sender)
    {
        if (!sender.Contains('@') && !sender.Any(char.IsAsciiDigit))
        {
            var number = ContactNameResolver.PhoneForDisplayName(this, sender);
            if (number is not null)
            {
                return number;
            }
        }
        return ContactKeyNormalizer.NormalizeAny(sender);
    }
}
