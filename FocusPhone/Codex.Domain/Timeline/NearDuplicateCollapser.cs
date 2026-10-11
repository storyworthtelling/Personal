namespace Codex.Domain.Timeline;

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

    // Separators Fi puts between its wrapper ("Voicemail from …") and the transcript.
    private static readonly char[] WrapperSeparators = [':', '—', '–', '-'];

    public static IReadOnlyList<TimelineItem> Collapse(IReadOnlyList<TimelineItem> items)
    {
        var providerTexts = items.Where(item => IsProviderText(item) || IsFiVoicemail(item)).ToList();
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

    // A Fi voicemail is the transcript SMS, read as a voicemail (FiVoicemail.TryParse). It is
    // the provider's record of that message, so it shadows the notification copy the same way
    // a provider text does — but its snippet is only the transcript, not the wrapped body the
    // notification shows, so the text must end with it rather than equal it.
    private static bool IsFiVoicemail(TimelineItem item) =>
        item.Kind == TimelineItemKind.Voicemail && HasPrefix(item, "fivm:");

    private static bool IsShadowedNotificationText(TimelineItem item, List<TimelineItem> providerTexts)
    {
        if (item.Kind != TimelineItemKind.Message || IsProviderText(item))
        {
            return false;
        }
        return providerTexts.Any(provider =>
            provider.Incoming == item.Incoming
            && (IsFiVoicemail(provider) ? IsWrappedTranscript(item.Snippet, provider.Snippet) : SnippetsMatch(provider, item))
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

    /// <summary>
    /// True when <paramref name="notificationText"/> is the wrapped form of the Fi voicemail
    /// whose shown words are <paramref name="transcript"/>. With words, the transcript must sit
    /// after a wrapper separator (Fi's own "(0:04):" or a colon); the separator is what stops a
    /// reply like "Oh, hi" from matching a transcript of "hi". With no words the shown label is
    /// "Voicemail", and only Fi's own footer identifies its notification copy.
    /// </summary>
    private static bool IsWrappedTranscript(string? notificationText, string? transcript)
    {
        var text = (notificationText ?? "").Trim();
        var words = (transcript ?? "").Trim();
        if (words.Length == 0 || text.Length == 0)
        {
            return false;
        }
        if (words == "Voicemail")
        {
            return text.Contains(VoicemailTranscript.ListenFooter, StringComparison.OrdinalIgnoreCase);
        }

        var at = text.IndexOf(words, StringComparison.Ordinal);
        if (at <= 0)
        {
            return false;
        }
        var wrapper = text[..at].TrimEnd();
        return wrapper.Length > 0 && WrapperSeparators.Contains(wrapper[^1]);
    }

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
