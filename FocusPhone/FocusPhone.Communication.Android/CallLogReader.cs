using Android.Content;
using Android.Provider;
using FocusPhone.Domain.Timeline;

namespace FocusPhone.Communication;

/// <summary>
/// Reads the system call log (Google Phone remains the dialer; we only
/// observe) into timeline items keyed by normalized phone number.
/// </summary>
public static class CallLogReader
{
    public static IReadOnlyList<TimelineItem> ReadAll(Context context)
    {
        var items = new List<TimelineItem>();
        using var cursor = context.ContentResolver!.Query(
            CallLog.Calls.ContentUri!,
            [CallLog.Calls.InterfaceConsts.Id, CallLog.Calls.Number,
                CallLog.Calls.Date, CallLog.Calls.Type, CallLog.Calls.Duration],
            null, null, null);
        if (cursor is null)
        {
            return items;
        }

        while (cursor.MoveToNext())
        {
            var id = cursor.GetString(0)!;
            var number = cursor.GetString(1) ?? "";
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(2));
            var type = (CallType)cursor.GetInt(3);
            var duration = TimeSpan.FromSeconds(cursor.GetLong(4));

            var incoming = type is CallType.Incoming or CallType.Missed or CallType.Rejected;
            var snippet = type switch
            {
                CallType.Missed => "Missed call",
                CallType.Rejected => "Declined call",
                CallType.Incoming => $"Incoming call ({duration:mm\\:ss})",
                _ => $"Outgoing call ({duration:mm\\:ss})",
            };

            items.Add(new TimelineItem(
                TimelineItemKind.Call,
                ContactKeyNormalizer.NormalizePhone(number),
                timestamp, snippet, incoming, id));
        }
        return items;
    }
}
