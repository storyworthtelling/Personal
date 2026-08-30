using Android.Content;
using Android.Provider;
using Codex.Domain.Timeline;

namespace Post;

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
            items.Add(CallRecordMapper.Map(
                cursor.GetString(0)!,
                cursor.GetString(1) ?? "",
                cursor.GetLong(2),
                cursor.GetInt(3),
                cursor.GetLong(4)));
        }
        return items;
    }
}
