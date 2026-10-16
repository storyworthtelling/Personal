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
    /// An incoming group MMS's address list usually includes this phone too. When the phone
    /// can say its own number (<paramref name="ownNumbers"/>) it is left out; when it cannot,
    /// it stays — an extra name in a group header is a smaller error than a wrongly dropped
    /// one.
    ///
    /// Each address in <paramref name="allAddresses"/> is normalized once to decide group
    /// membership and build <see cref="TimelineItem.Participants"/>, and the first raw
    /// spelling seen for each distinct normalized key survives as the matching entry in
    /// <see cref="TimelineItem.ParticipantAddresses"/> — the two lists describe the same
    /// people in the same order, one for matching/display, one for anything that has to
    /// actually reach the number.
    /// </summary>
    public static TimelineItem? MapMms(
        string id, long dateSeconds, int messageBox, string? address, string? text,
        long threadId = 0, IReadOnlyList<string>? allAddresses = null,
        IReadOnlyList<AttachmentRef>? attachments = null, IReadOnlyCollection<string>? ownNumbers = null)
    {
        if (string.IsNullOrEmpty(address) || messageBox is not (MmsInbox or MmsSent))
        {
            return null;
        }

        // One raw address survives per distinct normalized key (first seen), so
        // ParticipantAddresses and Participants describe the same people in the same
        // order — the normalized list for matching/display, the raw one for anything
        // that has to actually reach the number, country code included.
        var own = (ownNumbers ?? []).Select(ContactKeyNormalizer.NormalizePhone).ToHashSet(StringComparer.Ordinal);
        var ownListed = false;
        var byNormalizedKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in allAddresses ?? [])
        {
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }
            var key = ContactKeyNormalizer.NormalizePhone(raw);
            if (own.Contains(key))
            {
                ownListed = true;
                continue;
            }
            byNormalizedKey.TryAdd(key, raw);
        }
        var participants = byNormalizedKey.Keys.ToList();
        var participantAddresses = byNormalizedKey.Values.ToList();
        // Sender-plus-this-phone is a conversation of two; once this phone has been taken out
        // of the list, any second person makes it a group.
        var isGroup = participants.Count > (ownListed ? 1 : 2);
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
            ParticipantAddresses = isGroup ? participantAddresses : [],
            Attachments = attachmentList,
        };
    }
}
