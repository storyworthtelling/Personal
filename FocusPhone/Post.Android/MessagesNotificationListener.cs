using Android.App;
using Android.Service.Notification;
using Codex.Domain.Post;
using Codex.Domain.Timeline;

namespace Post;

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
        // Voicemail no longer comes through here. Google Fi's "Voicemails by text" delivers
        // the transcript as an SMS from the caller's own number, which is a durable record
        // in the provider rather than a one-shot Post has to be running to catch — and a
        // notification missed while the phone was off is gone. Capturing both would also
        // put two rows in the timeline for one message.
        if (sbn.PackageName == GoogleMessagesPackage)
        {
            CaptureMessages(sbn);
        }
    }

    public override void OnListenerConnected()
    {
        base.OnListenerConnected();
        PostDiagnostics.LogText("Notifications", "Notification capture connected");
    }

    // Android unbinds a listener under memory pressure or after a crash, and everything
    // Google Messages notifies until it rebinds never reaches Post — worth a line, since
    // it is one of the ways an RCS message goes missing.
    public override void OnListenerDisconnected()
    {
        base.OnListenerDisconnected();
        PostDiagnostics.LogText("Notifications", "Notification capture disconnected");
    }

    private void CaptureMessages(StatusBarNotification sbn)
    {
        var style = AndroidX.Core.App.NotificationCompat.MessagingStyle
            .ExtractMessagingStyleFromNotification(sbn.Notification!);
        if (style is null)
        {
            return;
        }

        // The shortcut id is Messages' own id for the conversation; a group threads on it.
        var notice = new MessagingNotice(
            sbn.Notification!.ShortcutId,
            style.ConversationTitle?.ToString(),
            style.IsGroupConversation,
            style.Messages
                .Select(message => new NoticeMessage(
                    message.Person?.Name?.ToString(), message.Timestamp, message.Text?.ToString()))
                .ToList());
        PostDiagnostics.LogText("Notifications", TextTrail.Notice(notice, DateTimeOffset.UtcNow));

        var items = MessagingNoticeMapper.Map(notice, ContactKey);
        Cache(items.ToList());

        var groupId = items.FirstOrDefault(item => item.IsGroup)?.GroupId;
        if (groupId is not null)
        {
            try
            {
                GroupNameStore.Learn(this, GroupNames.KeyFor(groupId), MessagingNoticeMapper.GroupTitle(notice));
            }
            catch (Exception e)
            {
                PostDiagnostics.LogWarning("Notifications", "Could not record a group's name", e);
            }
        }
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
            PostDiagnostics.LogWarning("Notifications", "Could not cache notification items", e);
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
