namespace FocusPhone.Domain.Timeline;

/// <summary>
/// Collapses the same real-world event captured by two sources into one item.
/// An incoming text lands in both the SMS provider (SourceId sms:/mms:) and
/// the notification listener (SourceId {key}:{millis}), and a voicemail lands
/// in both the call log (raw row id) and the dialer notification (SourceId
/// vm:...), so (kind, source id) dedup cannot catch them. The provider wins
/// for texts (the listener's remaining job is RCS, which never reaches the
/// provider); the notification wins for voicemails (it carries the
/// transcription). Notification items may be keyed by display name rather
/// than number, so a non-numeric key matches across contact keys.
/// </summary>
public static class NearDuplicateCollapser
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(3);

    public static IReadOnlyList<TimelineItem> Collapse(IReadOnlyList<TimelineItem> items)
    {
        var providerTexts = items.Where(IsProviderText).ToList();
        var voicemailNotifications = items
            .Where(item => item.Kind == TimelineItemKind.Voicemail && HasPrefix(item, "vm:"))
            .ToList();

        return items
            .Where(item => !IsShadowedNotificationText(item, providerTexts))
            .Where(item => !IsShadowedCallLogVoicemail(item, voicemailNotifications))
            .ToList();
    }

    private static bool IsProviderText(TimelineItem item) =>
        item.Kind == TimelineItemKind.Message && (HasPrefix(item, "sms:") || HasPrefix(item, "mms:"));

    private static bool IsShadowedNotificationText(TimelineItem item, List<TimelineItem> providerTexts)
    {
        if (item.Kind != TimelineItemKind.Message || IsProviderText(item))
        {
            return false;
        }
        return providerTexts.Any(provider =>
            provider.Incoming == item.Incoming
            && SnippetsMatch(provider, item)
            && WithinWindow(provider, item)
            && KeysCompatible(item, provider));
    }

    private static bool IsShadowedCallLogVoicemail(TimelineItem item, List<TimelineItem> notifications)
    {
        if (item.Kind != TimelineItemKind.Voicemail || HasPrefix(item, "vm:"))
        {
            return false;
        }
        return notifications.Any(notification =>
            WithinWindow(notification, item) && KeysCompatible(notification, item));
    }

    private static bool HasPrefix(TimelineItem item, string prefix) =>
        item.SourceId.StartsWith(prefix, StringComparison.Ordinal);

    private static bool SnippetsMatch(TimelineItem a, TimelineItem b) =>
        (a.Snippet ?? "").Trim() == (b.Snippet ?? "").Trim();

    private static bool WithinWindow(TimelineItem a, TimelineItem b) =>
        (a.TimestampUtc - b.TimestampUtc).Duration() <= Window;

    /// <summary>
    /// The notification key matches when equal, or when it is a display name
    /// (non-numeric) that could not be resolved to the provider's number.
    /// </summary>
    private static bool KeysCompatible(TimelineItem notification, TimelineItem other) =>
        notification.ContactKey == other.ContactKey
        || !notification.ContactKey.All(char.IsAsciiDigit);
}
