namespace Storyworthtelling.Diagnostics;

/// <summary>
/// O12: upload weekly, just before the Sunday reader. The daily worker asks <see cref="Due"/>: yes from
/// Saturday 18:00 on the phone's own clock until a confirmed upload lands that week. A phone offline on
/// Saturday uploads at its first connection; whatever lands after the Sunday run waits for the next one.
/// </summary>
public static class UploadWindow
{
    public const DayOfWeek OpensOn = DayOfWeek.Saturday;
    public static readonly TimeSpan OpensAt = TimeSpan.FromHours(18);

    /// <param name="lastConfirmedUpload">When an upload last read back and matched; null if never.</param>
    /// <param name="now">The phone's local time, with its offset.</param>
    public static bool Due(DateTimeOffset? lastConfirmedUpload, DateTimeOffset now) =>
        lastConfirmedUpload is null || lastConfirmedUpload.Value < Start(now);

    /// <summary>The most recent Saturday 18:00 at or before <paramref name="now"/>, in its own offset.</summary>
    public static DateTimeOffset Start(DateTimeOffset now)
    {
        var daysBack = ((int)now.DayOfWeek - (int)OpensOn + 7) % 7;
        var candidate = new DateTimeOffset(now.Date.AddDays(-daysBack) + OpensAt, now.Offset);
        return candidate > now ? candidate.AddDays(-7) : candidate;
    }
}
