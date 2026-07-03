namespace FocusPhone.Domain.Timeline;

/// <summary>
/// Recognizes Google Phone voicemail notifications and turns them into
/// timeline items carrying the transcription. Direct voicemail reads need a
/// privileged permission unavailable even to a device owner, so notification
/// capture is the only source of transcriptions; the match is deliberately
/// tolerant because the exact channel/title strings are device behavior.
/// </summary>
public static class VoicemailNotificationParser
{
    public const string DialerPackage = "com.google.android.dialer";

    private const string TitlePrefix = "Voicemail from ";

    public static TimelineItem? TryParse(
        string? packageName, string? channelId, string? title, string? text, long whenMillis)
    {
        if (packageName != DialerPackage || !MentionsVoicemail(channelId) && !MentionsVoicemail(title))
        {
            return null;
        }

        var caller = title ?? "";
        if (caller.StartsWith(TitlePrefix, StringComparison.OrdinalIgnoreCase))
        {
            caller = caller[TitlePrefix.Length..];
        }
        var contactKey = ContactKeyNormalizer.NormalizeAny(caller);
        if (contactKey.Length == 0)
        {
            contactKey = "voicemail";
        }

        return new TimelineItem(
            TimelineItemKind.Voicemail,
            contactKey,
            DateTimeOffset.FromUnixTimeMilliseconds(whenMillis),
            string.IsNullOrWhiteSpace(text) ? "Voicemail" : text,
            Incoming: true,
            SourceId: $"vm:{contactKey}:{whenMillis}");
    }

    private static bool MentionsVoicemail(string? value) =>
        value is not null && value.Contains("voicemail", StringComparison.OrdinalIgnoreCase);
}
