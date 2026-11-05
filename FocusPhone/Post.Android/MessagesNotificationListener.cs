using Android.App;
using Android.Service.Notification;
using Codex.Domain.Post;
using Codex.Domain.Timeline;

namespace Post;

/// <summary>
/// Google Messages' notifications, read for three jobs. It is the bridge's doorbell: a
/// Messages notification reconnects the Messages-for-Web bridge if it was down (Android keeps
/// Post's process alive while this listener is bound, which is what lets the bridge stay up
/// without a notification of its own). It is the backup copy of a message caught while the
/// bridge was down — the bridge's own copy outranks it at merge time
/// (NearDuplicateCollapser). And while a pairing waits, it keeps Messages' confirmation
/// notification for the pairing screen, since the lockdown hides the status bar it would
/// otherwise appear in. Listener access is granted during provisioning.
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
            // A Messages notification is the doorbell for the bridge: if it was down, this
            // brings it back, and it delivers the message with its real conversation.
            var bridge = GmBridgeClient.Shared(this);
            bridge.ConnectIfPaired();
            if (!CaptureMessages(sbn) && bridge.IsPairing)
            {
                // Not a message: while a pairing waits, this is Messages asking to confirm it,
                // which the hidden status bar would never show. The pairing screen opens it.
                PairingPrompt.Offer(sbn.Notification.ContentIntent);
            }
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

    // True when the notification was a conversation's (MessagingStyle), whether or not it held anything new.
    private bool CaptureMessages(StatusBarNotification sbn)
    {
        var style = AndroidX.Core.App.NotificationCompat.MessagingStyle
            .ExtractMessagingStyleFromNotification(sbn.Notification!);
        if (style is null)
        {
            return false;
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
        return true;
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
