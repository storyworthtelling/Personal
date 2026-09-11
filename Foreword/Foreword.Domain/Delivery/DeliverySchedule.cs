namespace Foreword.Domain.Delivery;

/// <summary>What to do with the periodic work a phone already has.</summary>
public enum SchedulePolicy
{
    /// <summary>Leave the scheduled work exactly as it is.</summary>
    Keep,

    /// <summary>Cancel the scheduled work and enqueue it again at the time the app now wants.</summary>
    Replace
}

/// <summary>
/// Whether the phone's scheduled morning still matches the morning the app intends.
///
/// <para>WorkManager persists a periodic work request, initial delay and all, and
/// <c>ExistingPeriodicWorkPolicy.Keep</c> means a phone that already has a row keeps it. That is
/// the right default — without it, every app launch would reset the initial delay on a job that
/// has not run yet, so a fresh install whose owner opens the app at nine would be pushed to
/// tomorrow, and again the next morning, indefinitely.</para>
///
/// <para>But it also means moving the delivery time in code moves nothing on a phone that has
/// already run the app. The schedule would go on firing at the old hour while Settings printed the
/// new one from the constant, and nothing would report the disagreement. So the app remembers the
/// time the work was actually scheduled for and replaces the work only when that stamp disagrees
/// with the time it now wants — which keeps Keep's protection and fixes what Keep froze.</para>
/// </summary>
public static class DeliverySchedule
{
    /// <summary>
    /// The stored shape of a scheduled time. Zero-padded 24-hour, because this value is compared
    /// against what an older launch wrote: 06:30 and 6:30 as two spellings of one time would look
    /// like a changed schedule on every launch and re-enqueue the work every time.
    /// </summary>
    public static string Stamp(int hour, int minute) => $"{hour:D2}:{minute:D2}";

    /// <summary>
    /// <paramref name="scheduled"/> is the stamp the last successful schedule wrote, or null on a
    /// phone that has never written one. Null is <see cref="SchedulePolicy.Replace"/> because two
    /// phones arrive here that way and both want it: the phone upgrading from a build that had no
    /// stamp, whose work is scheduled for the old hour, and the fresh install, which has no work
    /// at all and is simply enqueued.
    /// </summary>
    public static SchedulePolicy Decide(int hour, int minute, string? scheduled) =>
        string.Equals(scheduled?.Trim(), Stamp(hour, minute), StringComparison.Ordinal)
            ? SchedulePolicy.Keep
            : SchedulePolicy.Replace;

    /// <summary>
    /// How long from <paramref name="now"/> until the next <paramref name="hour"/>:<paramref
    /// name="minute"/>, read on <paramref name="now"/>'s own clock.
    ///
    /// <para>The offset it is given is the offset it aims at, which is the whole point. The
    /// shipped version read <c>UtcNow</c> and then built its target at an offset of zero, so the
    /// hour it aimed at was an hour UTC while the constant beside it was documented as local and
    /// the Settings screen printed it as a local wall-clock time. At 10:45 that was nearly
    /// invisible — 10:45 UTC is a quarter to seven on the east coast, which looks like the morning
    /// delivery it was meant to be. At 6:30 it is half past one in the morning.</para>
    /// </summary>
    public static TimeSpan DelayUntilNext(DateTimeOffset now, int hour, int minute)
    {
        var target = new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset);
        if (target <= now)
            target = target.AddDays(1);
        return target - now;
    }
}
