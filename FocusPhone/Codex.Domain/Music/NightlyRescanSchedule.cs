namespace Codex.Domain.Music;

/// <summary>
/// When the nightly library rescan runs, and how long from any given moment until then.
///
/// <para>Modeled on Foreword's <c>DeliverySchedule.DelayUntilNext</c>: WorkManager persists
/// a periodic request's initial delay, so the worker aims itself at a specific hour by
/// computing that delay fresh at schedule time rather than by a raw always-24h period,
/// which would drift to whatever time of day the app happened to first schedule it (as
/// <c>FridayPicksWorker</c>'s daily backbone deliberately does, since Friday-gating inside
/// <c>DoWork</c> is all it needs — a genuinely nightly job needs the hour itself pinned
/// down).</para>
///
/// <para>The offset the caller passes in is the offset the target is built in, which is
/// the whole point (see <c>DeliverySchedule</c>'s own note on this): building the target at
/// UTC while <paramref name="now"/> is a local wall-clock time would aim the "night" scan at
/// the wrong hour everywhere except UTC+0.</para>
/// </summary>
public static class NightlyRescanSchedule
{
    /// <summary>3 AM, owner-local — late enough that the owner is almost certainly asleep,
    /// early enough that the scan (and, if the taste/discovery workers also fire overnight,
    /// them too) is long done before a plausible first open.</summary>
    public const int ScanHour = 3;

    public const int ScanMinute = 0;

    public static TimeSpan DelayUntilNext(DateTimeOffset now) => DelayUntilNext(now, ScanHour, ScanMinute);

    public static TimeSpan DelayUntilNext(DateTimeOffset now, int hour, int minute)
    {
        var target = new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset);
        if (target <= now)
        {
            target = target.AddDays(1);
        }

        return target - now;
    }
}
