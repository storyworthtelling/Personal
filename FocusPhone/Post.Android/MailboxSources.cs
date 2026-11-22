using Android.Content;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Ui;

namespace Post;

/// <summary>
/// What the Mailbox is read from, shared with Dash's listener (P14 B2) so the wrist is shown what the phone shows: the
/// live tail (the call log, and the phone's text store while the bridge is unpaired) and the timeline cache, a page at a
/// time, newest first. Off the main thread: every read is a provider query.
/// </summary>
internal static class MailboxSources
{
    /// <summary>
    /// The timeline cache is read a page at a time (newest first) so a large history never has to be pulled into memory
    /// at once; the Mailbox loads more as the owner scrolls, and the wrist sees the first page.
    /// </summary>
    public const int CachePageSize = 300;

    /// <summary>The small recent tail read live on every reload: calls, and texts while the provider is the record.</summary>
    public static List<TimelineItem> LiveTail(Context context)
    {
        var live = new List<TimelineItem>();
        try
        {
            live.AddRange(CallLogReader.ReadAll(context));
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_CALL_LOG not granted yet; show what we have, and say so once (audit 08 §24).
            HeadLog.Denied("READ_CALL_LOG");
        }
        try
        {
            if (TextSources.ReadsProvider(GmBridgeClient.Shared(context).IsPaired))
            {
                live.AddRange(SmsLogReader.ReadAll(context));
            }
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_SMS not granted yet; show what we have, and say so once (audit 08 §24).
            HeadLog.Denied("READ_SMS");
        }
        return live;
    }
}
