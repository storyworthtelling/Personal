using Android.Content;
using Android.Provider;
using Codex.Domain.Post;
using Codex.Domain.Timeline;

namespace Post;

/// <summary>
/// Reads the system SMS/MMS store into timeline items keyed by normalized phone number.
/// Post is the SMS app now rather than an observer, so this is reading its own record.
/// Like
/// READ_CALL_LOG, the READ_SMS permission is hard-restricted and only takes
/// effect on a whitelisted install; callers catch SecurityException.
/// </summary>
public static class SmsLogReader
{
    // Telephony.Mms.Addr type values: 137 = from, 151 = to.
    private const string MmsAddrFrom = "137";
    private const string MmsAddrTo = "151";
    internal const int MmsBoxInbox = 1;

    public static IReadOnlyList<TimelineItem> ReadAll(Context context)
    {
        var items = new List<TimelineItem>();
        ReadSms(context, items);
        ReadMms(context, items);
        return items;
    }

    private static void ReadSms(Context context, List<TimelineItem> items)
    {
        using var cursor = context.ContentResolver!.Query(
            Telephony.Sms.ContentUri!,
            ["_id", "address", "date", "type", "body"],
            null, null, null);
        if (cursor is null)
        {
            return;
        }
        while (cursor.MoveToNext())
        {
            var address = cursor.IsNull(1) ? null : cursor.GetString(1);
            var body = cursor.IsNull(4) ? null : cursor.GetString(4);
            var when = DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(2));

            // A Google Fi voicemail arrives as an ordinary SMS from the caller's own
            // number, so it has to be recognised BEFORE it is mapped as a text — otherwise
            // it threads with that person's messages instead of beside their calls.
            var item = FiVoicemail.TryParse(address, body, when)
                ?? SmsRecordMapper.Map(
                    cursor.GetString(0)!, address, cursor.GetLong(2), cursor.GetInt(3), body);
            if (item is not null)
            {
                items.Add(item);
            }
        }
    }

    private static void ReadMms(Context context, List<TimelineItem> items)
    {
        // This phone's number is left out of a group's members, so it neither heads the
        // group nor receives its own reply.
        var ownNumbers = OwnNumbers.Read(context);
        using var cursor = context.ContentResolver!.Query(
            Telephony.Mms.ContentUri!,
            ["_id", "date", "msg_box", "thread_id"],
            null, null, null);
        if (cursor is null)
        {
            return;
        }
        while (cursor.MoveToNext())
        {
            var id = cursor.GetString(0)!;
            var messageBox = cursor.GetInt(2);
            var threadId = cursor.IsNull(3) ? 0 : cursor.GetLong(3);
            var item = SmsRecordMapper.MapMms(
                id,
                cursor.GetLong(1),
                messageBox,
                ReadMmsAddress(context, id, messageBox),
                ReadMmsText(context, id),
                threadId,
                ReadMmsAllAddresses(context, id),
                ReadMmsAttachments(context, id),
                ownNumbers);
            if (item is not null)
            {
                items.Add(item);
            }
        }
    }

    // Internal rather than private: MmsDownloader's download-completion receiver reads the
    // same two fields off the row it just landed, to raise a notification without a second,
    // duplicate copy of this query.
    internal static string? ReadMmsAddress(Context context, string id, int messageBox)
    {
        var addrType = messageBox == MmsBoxInbox ? MmsAddrFrom : MmsAddrTo;
        using var cursor = context.ContentResolver!.Query(
            Android.Net.Uri.Parse($"content://mms/{id}/addr")!,
            ["address"], "type = ?", [addrType], null);
        while (cursor is not null && cursor.MoveToNext())
        {
            var address = cursor.IsNull(0) ? null : cursor.GetString(0);
            if (!string.IsNullOrEmpty(address) && address != "insert-address-token")
            {
                return address;
            }
        }
        return null;
    }

    /// <summary>
    /// Every address on the message — from, to, cc and bcc together, whichever type each
    /// row is — which is what SmsRecordMapper needs to tell a group text apart from an
    /// ordinary one. Unlike <see cref="ReadMmsAddress"/> this deliberately does not filter
    /// by type: a group's other members show up as "to" rows the same as the one recipient
    /// an ordinary message has, and the count is the whole signal.
    /// </summary>
    internal static List<string> ReadMmsAllAddresses(Context context, string id)
    {
        var addresses = new List<string>();
        using var cursor = context.ContentResolver!.Query(
            Android.Net.Uri.Parse($"content://mms/{id}/addr")!,
            ["address"], null, null, null);
        while (cursor is not null && cursor.MoveToNext())
        {
            var address = cursor.IsNull(0) ? null : cursor.GetString(0);
            if (!string.IsNullOrEmpty(address) && address != "insert-address-token")
            {
                addresses.Add(address);
            }
        }
        return addresses;
    }

    internal static string? ReadMmsText(Context context, string id)
    {
        using var cursor = context.ContentResolver!.Query(
            Android.Net.Uri.Parse("content://mms/part")!,
            ["text", "_data"], "mid = ? AND ct = 'text/plain'", [id], null);
        while (cursor is not null && cursor.MoveToNext())
        {
            // When the part body lives in a file (_data set) skip streaming it;
            // the mapper falls back to the [MMS] placeholder.
            if (cursor.IsNull(1) && !cursor.IsNull(0))
            {
                return cursor.GetString(0);
            }
        }
        return null;
    }

    // SMIL (application/smil) is the MMS layout description every multi-part message
    // carries alongside its real content — a stage direction, not something to show.
    private const string SmilContentType = "application/smil";

    /// <summary>
    /// The picture/video parts a message actually carries, as attachments the Mailbox can
    /// offer to open — this is the half of "receiving an MMS" that stops at a [MMS]
    /// placeholder without it. Bytes are not read here: only enough to build the reference
    /// (id, name, type), matching how email attachments are fetched lazily, on open.
    /// </summary>
    // Internal rather than private: MmsDownloadReceiver reads the same parts off a row it
    // just landed, to say "Photo"/"Video" in the notification instead of a bare placeholder.
    internal static List<AttachmentRef> ReadMmsAttachments(Context context, string id)
    {
        var attachments = new List<AttachmentRef>();
        using var cursor = context.ContentResolver!.Query(
            Android.Net.Uri.Parse("content://mms/part")!,
            ["_id", "ct", "fn", "name", "cl"],
            "mid = ? AND ct NOT LIKE 'text/%' AND ct <> ?", [id, SmilContentType], null);
        while (cursor is not null && cursor.MoveToNext())
        {
            var partId = cursor.IsNull(0) ? null : cursor.GetString(0);
            var contentType = cursor.IsNull(1) ? null : cursor.GetString(1);
            if (partId is null || string.IsNullOrEmpty(contentType))
            {
                continue;
            }
            var fileName = FirstNonEmpty(
                cursor.IsNull(2) ? null : cursor.GetString(2),
                cursor.IsNull(3) ? null : cursor.GetString(3),
                cursor.IsNull(4) ? null : cursor.GetString(4));
            // The byte size lives with the part's own data, not as a column worth a second
            // query for a value the attachment sheet only ever shows as a courtesy; 0 reads
            // as "size unknown" the same way a missing size does elsewhere in Post.
            attachments.Add(new AttachmentRef($"mms-part:{partId}", fileName, contentType, SizeBytes: 0));
        }
        return attachments;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrEmpty(value));
}
