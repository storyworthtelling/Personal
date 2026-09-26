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

    /// <summary>
    /// <paramref name="allAddresses"/> is every address on the message (from + to + cc +
    /// bcc), which is what tells a real group text apart from an ordinary 1:1 MMS — a plain
    /// message is sender-plus-you (two addresses); anything with a third party on it is a
    /// group. <paramref name="threadId"/> is the system MMS/SMS provider's own conversation
    /// id, reused as the <see cref="TimelineItem.GroupId"/> rather than building one from the
    /// address list, since Android already canonicalizes "who is this conversation with"
    /// consistently across messages.
    ///
    /// Participants can end up including the device's own number: an incoming group MMS's
    /// own address list often lists every recipient, yourself included, and there is no
    /// reliable way from the provider alone to tell which one that is. Left as-is rather than
    /// guessed at — an extra name in a group header is a smaller error than a wrongly
    /// dropped one.
    /// </summary>
    public static TimelineItem? MapMms(
        string id, long dateSeconds, int messageBox, string? address, string? text,
        long threadId = 0, IReadOnlyList<string>? allAddresses = null,
        IReadOnlyList<AttachmentRef>? attachments = null)
    {
        if (string.IsNullOrEmpty(address) || messageBox is not (MmsInbox or MmsSent))
        {
            return null;
        }

        var participants = (allAddresses ?? [])
            .Where(a => !string.IsNullOrEmpty(a))
            .Select(ContactKeyNormalizer.NormalizePhone)
            .Distinct()
            .ToList();
        var isGroup = participants.Count > 2;
        var attachmentList = attachments ?? [];

        return new TimelineItem(
            TimelineItemKind.Message,
            ContactKeyNormalizer.NormalizePhone(address),
            // Unlike every other telephony table, the MMS date column is in seconds.
            DateTimeOffset.FromUnixTimeSeconds(dateSeconds),
            string.IsNullOrEmpty(text) ? AttachmentSummary.Describe(attachmentList) : text,
            Incoming: messageBox == MmsInbox,
            SourceId: $"mms:{id}")
        {
            GroupId = isGroup ? $"mms:{threadId}" : null,
            Participants = isGroup ? participants : [],
            Attachments = attachmentList,
        };
    }
}
