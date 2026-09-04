namespace Codex.Domain.Timeline;

/// <summary>
/// Maps a system call-log row to a timeline item. Voicemail-type rows become
/// their own kind so they render distinctly and never collide with a call
/// sharing the same call-log id.
/// </summary>
public static class CallRecordMapper
{
    // CallLog.Calls type constants.
    private const int Incoming = 1;
    private const int Missed = 3;
    private const int Voicemail = 4;
    private const int Rejected = 5;

    public static TimelineItem Map(string id, string number, long dateMillis, int type, long durationSeconds)
    {
        var contactKey = ContactKeyNormalizer.NormalizePhone(number);
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dateMillis);
        if (type == Voicemail)
        {
            return new TimelineItem(
                TimelineItemKind.Voicemail, contactKey, timestamp, "Voicemail", Incoming: true, id);
        }

        var duration = TimeSpan.FromSeconds(durationSeconds);
        var incoming = type is Incoming or Missed or Rejected;
        var snippet = type switch
        {
            Missed => "Missed call",
            Rejected => "Declined call",
            Incoming => $"Incoming call ({duration:mm\\:ss})",
            _ => $"Outgoing call ({duration:mm\\:ss})",
        };
        return new TimelineItem(TimelineItemKind.Call, contactKey, timestamp, snippet, incoming, id);
    }
}
