namespace Constitution.Domain.ReadBack;

/// <summary>
/// When the morning read-back runs: after Foreword's delivery window (D87), on the phone's own clock. Foreword
/// delivers at half past six; the read-back waits three quarters of an hour more so a slow morning there has finished.
/// </summary>
public static class ReadBackSchedule
{
    public const int Hour = 7;

    public const int Minute = 15;

    /// <summary>
    /// How long from <paramref name="now"/> until the next <see cref="Hour"/>:<see cref="Minute"/>, read on
    /// <paramref name="now"/>'s own offset, so the hour is the one the owner reads on their clock. Exactly on the hour waits a day.
    /// </summary>
    public static TimeSpan DelayUntilNext(DateTimeOffset now)
    {
        var target = new DateTimeOffset(now.Year, now.Month, now.Day, Hour, Minute, 0, now.Offset);
        if (target <= now)
        {
            target = target.AddDays(1);
        }

        return target - now;
    }
}
