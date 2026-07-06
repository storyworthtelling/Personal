using Android.Content;
using Android.Provider;
using FocusPhone.Domain.Timeline;

namespace FocusPhone.Communication;

/// <summary>
/// Reads the system SMS/MMS store (Google Messages remains the SMS app; we
/// only observe) into timeline items keyed by normalized phone number. Like
/// READ_CALL_LOG, the READ_SMS permission is hard-restricted and only takes
/// effect on a whitelisted install; callers catch SecurityException.
/// </summary>
public static class SmsLogReader
{
    // Telephony.Mms.Addr type values: 137 = from, 151 = to.
    private const string MmsAddrFrom = "137";
    private const string MmsAddrTo = "151";
    private const int MmsBoxInbox = 1;

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
            var item = SmsRecordMapper.Map(
                cursor.GetString(0)!,
                cursor.IsNull(1) ? null : cursor.GetString(1),
                cursor.GetLong(2),
                cursor.GetInt(3),
                cursor.IsNull(4) ? null : cursor.GetString(4));
            if (item is not null)
            {
                items.Add(item);
            }
        }
    }

    private static void ReadMms(Context context, List<TimelineItem> items)
    {
        using var cursor = context.ContentResolver!.Query(
            Telephony.Mms.ContentUri!,
            ["_id", "date", "msg_box"],
            null, null, null);
        if (cursor is null)
        {
            return;
        }
        while (cursor.MoveToNext())
        {
            var id = cursor.GetString(0)!;
            var messageBox = cursor.GetInt(2);
            var item = SmsRecordMapper.MapMms(
                id,
                cursor.GetLong(1),
                messageBox,
                ReadMmsAddress(context, id, messageBox),
                ReadMmsText(context, id));
            if (item is not null)
            {
                items.Add(item);
            }
        }
    }

    private static string? ReadMmsAddress(Context context, string id, int messageBox)
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

    private static string? ReadMmsText(Context context, string id)
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
}
