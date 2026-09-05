namespace Codex.Domain.Timeline;

/// <summary>
/// Maps system SMS/MMS provider rows to timeline items. Only delivered
/// messages (inbox/sent) appear in the feed; drafts and undelivered states
/// map to null. The sms:/mms: SourceId prefixes mark these rows as the
/// authoritative source when collapsing notification-captured duplicates.
/// </summary>
public static class SmsRecordMapper
{
    // Telephony.Sms type constants.
    private const int SmsInbox = 1;
    private const int SmsSent = 2;

    // Telephony.Mms msg_box constants.
    private const int MmsInbox = 1;
    private const int MmsSent = 2;

    public static TimelineItem? Map(string id, string? address, long dateMillis, int type, string? body)
    {
        if (string.IsNullOrEmpty(address) || type is not (SmsInbox or SmsSent))
        {
            return null;
        }
        return new TimelineItem(
            TimelineItemKind.Message,
            ContactKeyNormalizer.NormalizePhone(address),
            DateTimeOffset.FromUnixTimeMilliseconds(dateMillis),
            body,
            Incoming: type == SmsInbox,
            SourceId: $"sms:{id}");
    }

    public static TimelineItem? MapMms(string id, long dateSeconds, int messageBox, string? address, string? text)
    {
        if (string.IsNullOrEmpty(address) || messageBox is not (MmsInbox or MmsSent))
        {
            return null;
        }
        return new TimelineItem(
            TimelineItemKind.Message,
            ContactKeyNormalizer.NormalizePhone(address),
            // Unlike every other telephony table, the MMS date column is in seconds.
            DateTimeOffset.FromUnixTimeSeconds(dateSeconds),
            string.IsNullOrEmpty(text) ? "[MMS]" : text,
            Incoming: messageBox == MmsInbox,
            SourceId: $"mms:{id}");
    }
}
