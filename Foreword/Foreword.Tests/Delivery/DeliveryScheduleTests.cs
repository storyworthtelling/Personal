using Foreword.Domain.Delivery;
using Xunit;

namespace Foreword.Tests.Delivery;

/// <summary>
/// The delivery time moved from 10:45 to 6:30, and moving the constant is not enough.
///
/// <para><c>Schedule</c> enqueues unique periodic work with <c>ExistingPeriodicWorkPolicy.Keep</c>,
/// which means exactly what it says: a phone that already has a row for this work keeps the row it
/// has, delay and all. So every phone that ever ran the old build would go on delivering at 10:45
/// forever, and nothing anywhere would report that the app and the phone disagreed — the Settings
/// screen would print 6:30 from the constant while the schedule fired at 10:45.</para>
///
/// <para>Keep was not a mistake, though: it is what stops every app launch from resetting the
/// initial delay on a job that has not run yet, which would starve a fresh install whose owner
/// opens the app after the morning window. So the answer is not to swap it for Update wholesale —
/// it is to remember the time the work was actually scheduled for, and replace the work only when
/// that disagrees with the time the app now wants.</para>
/// </summary>
public class DeliveryScheduleTests
{
    [Fact]
    public void APhoneThatHasNeverRecordedAScheduleIsReplaced()
    {
        // Two phones look identical here and both want the same answer. The upgrading phone has
        // work scheduled at the old hour and no stamp, because the build that scheduled it had no
        // stamp to write; the fresh install has no work at all. Replace is right for both: it
        // re-times the first and simply enqueues the second.
        Assert.Equal(SchedulePolicy.Replace, DeliverySchedule.Decide(6, 30, scheduled: null));
        Assert.Equal(SchedulePolicy.Replace, DeliverySchedule.Decide(6, 30, scheduled: ""));
    }

    [Fact]
    public void APhoneScheduledForADifferentTimeIsReplaced()
    {
        Assert.Equal(SchedulePolicy.Replace, DeliverySchedule.Decide(6, 30, DeliverySchedule.Stamp(10, 45)));
        Assert.Equal(SchedulePolicy.Replace, DeliverySchedule.Decide(6, 30, DeliverySchedule.Stamp(6, 0)));
        Assert.Equal(SchedulePolicy.Replace, DeliverySchedule.Decide(6, 30, DeliverySchedule.Stamp(7, 30)));
    }

    /// <summary>
    /// This is the case Keep exists for. Once the stamp agrees, every subsequent launch leaves the
    /// scheduled work alone — so opening the app at nine in the morning does not push the delivery
    /// to tomorrow, over and over, for as long as the owner keeps opening it.
    /// </summary>
    [Fact]
    public void APhoneAlreadyScheduledForThisTimeIsLeftAlone()
    {
        Assert.Equal(SchedulePolicy.Keep, DeliverySchedule.Decide(6, 30, DeliverySchedule.Stamp(6, 30)));
    }

    /// <summary>
    /// The stamp is persisted, so its shape is a stored shape: it is compared against what an
    /// older launch wrote. Zero-padded 24-hour, so 06:30 and 6:30 can never be read as two
    /// different schedules and replace the work on every launch.
    /// </summary>
    [Fact]
    public void TheStampIsZeroPaddedTwentyFourHour()
    {
        Assert.Equal("06:30", DeliverySchedule.Stamp(6, 30));
        Assert.Equal("10:45", DeliverySchedule.Stamp(10, 45));
        Assert.Equal("00:00", DeliverySchedule.Stamp(0, 0));
        Assert.Equal("23:05", DeliverySchedule.Stamp(23, 5));
    }

    /// <summary>
    /// Whitespace around a stored value is not a different schedule. A stamp read back from a
    /// preferences store that has been through an export, an edit or a restore can pick up a
    /// stray space, and re-enqueuing the work on every launch because of one would be the exact
    /// starvation Keep was protecting against.
    /// </summary>
    [Fact]
    public void SurroundingWhitespaceIsNotADifferentSchedule()
    {
        Assert.Equal(SchedulePolicy.Keep, DeliverySchedule.Decide(6, 30, " 06:30 "));
    }
}

/// <summary>
/// The delay to the next delivery, which was computed in the wrong clock.
///
/// <para><c>DelayUntilNext</c> read <c>DateTimeOffset.UtcNow</c> and then built its target with an
/// offset of <c>TimeSpan.Zero</c> — so the hour it aimed at was an hour <b>UTC</b>, while the
/// constant beside it was commented "in local time" and the Settings screen printed it as a local
/// wall-clock time. At the old 10:45 the mistake was nearly invisible: 10:45 UTC lands at a quarter
/// to seven in the morning on the east coast, which looks like a morning delivery and is roughly
/// when one arrived. Moving the constant to 6:30 is what exposes it, because 6:30 UTC is half past
/// one in the morning — the document would be delivered while the owner was asleep and Settings
/// would go on saying 6:30 am.</para>
///
/// <para>So the delay is computed from the offset it is given. The caller passes local now; the
/// hour means what the screen says it means.</para>
/// </summary>
public class DeliveryDelayTests
{
    // A fixed offset stands in for the phone's zone, so the test says nothing about where the
    // machine running it happens to be.
    private static readonly TimeSpan Eastern = TimeSpan.FromHours(-4);

    [Fact]
    public void TheTargetIsTheHourOnTheClockTheOwnerReads()
    {
        // Four in the morning, local. Half past six is later today, two and a half hours off.
        var now = new DateTimeOffset(2026, 9, 11, 4, 0, 0, Eastern);

        Assert.Equal(TimeSpan.FromHours(2.5), DeliverySchedule.DelayUntilNext(now, 6, 30));
    }

    [Fact]
    public void AnHourAlreadyPassedTodayIsTomorrows()
    {
        // Nine in the morning: half past six has been and gone, so the next one is tomorrow's.
        var now = new DateTimeOffset(2026, 9, 11, 9, 0, 0, Eastern);

        Assert.Equal(TimeSpan.FromHours(21.5), DeliverySchedule.DelayUntilNext(now, 6, 30));
    }

    /// <summary>
    /// The case that catches the UTC reading directly. Just before half past six in the evening
    /// local, 6:30 UTC has already passed, so a UTC computation would return most of a day. Read
    /// locally, the answer is twelve hours.
    /// </summary>
    [Fact]
    public void TheLocalEveningIsNotTheUtcMorning()
    {
        var now = new DateTimeOffset(2026, 9, 11, 18, 30, 0, Eastern);

        Assert.Equal(TimeSpan.FromHours(12), DeliverySchedule.DelayUntilNext(now, 6, 30));
    }

    [Fact]
    public void ExactlyOnTheHourIsTomorrowsRatherThanNow()
    {
        var now = new DateTimeOffset(2026, 9, 11, 6, 30, 0, Eastern);

        Assert.Equal(TimeSpan.FromDays(1), DeliverySchedule.DelayUntilNext(now, 6, 30));
    }
}
