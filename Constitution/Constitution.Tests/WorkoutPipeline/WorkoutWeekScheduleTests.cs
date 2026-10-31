using System.Text.RegularExpressions;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// When the weekly workout run wakes: a quarter to seven on the owner's own clock, after Foreword has delivered (half past six)
/// and before the read-back (a quarter past seven). The hour is the wall clock's, so it is worked out in the household's zone and
/// not on whatever offset the caller's instant happens to carry.
/// </summary>
public class WorkoutWeekScheduleTests
{
    private static TimeSpan Ours => new(WorkoutWeekSchedule.Hour, WorkoutWeekSchedule.Minute, 0);

    [Fact]
    public void ItRunsAtAQuarterToSeven()
    {
        Assert.Equal(new TimeSpan(6, 45, 0), Ours);
    }

    [Fact]
    public void ItRunsAfterForewordAndBeforeTheReadBack()
    {
        var foreword = ForewordsDeliveryTime();
        var readBack = new TimeSpan(ReadBackSchedule.Hour, ReadBackSchedule.Minute, 0);

        Assert.True(Ours - foreword >= TimeSpan.FromMinutes(10), $"The workout week at {Ours} is not clear of Foreword's {foreword}.");
        Assert.True(readBack - Ours >= TimeSpan.FromMinutes(15), $"The workout week at {Ours} is not clear of the read-back at {readBack}.");
    }

    [Fact]
    public void BeforeTheTimeItWaitsForToday()
    {
        var now = Est(11, 4, 5, 0);

        Assert.Equal(new TimeSpan(1, 45, 0), WorkoutWeekSchedule.DelayUntilNext(now, Zone));
    }

    [Fact]
    public void AfterTheTimeItWaitsForTomorrow()
    {
        var now = Est(11, 4, 12, 0);

        Assert.Equal(TimeSpan.FromHours(18) + TimeSpan.FromMinutes(45), WorkoutWeekSchedule.DelayUntilNext(now, Zone));
    }

    [Fact]
    public void ExactlyOnTheTimeWaitsADay()
    {
        Assert.Equal(TimeSpan.FromHours(24), WorkoutWeekSchedule.DelayUntilNext(Est(11, 4, 6, 45), Zone));
    }

    [Fact]
    public void ItReadsTheHouseholdsClockNotTheOffset()
    {
        // 11:00 UTC on 4 November is 6:00 am in the household's zone (UTC-5): three quarters of an hour to wait, whatever offset the caller's instant carries.
        var now = new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromMinutes(45), WorkoutWeekSchedule.DelayUntilNext(now, Zone));
    }

    [Fact]
    public void TheSpringNightIsAnHourShorter()
    {
        // 11 pm on Saturday 7 March 2026 (standard time) to 6:45 am on Sunday 8 March (daylight time): the clock jumps at 2, so only 6 h 45 min pass.
        var now = Est(3, 7, 23, 0);

        Assert.Equal(new TimeSpan(6, 45, 0), WorkoutWeekSchedule.DelayUntilNext(now, Zone));
    }

    [Fact]
    public void TheAutumnNightIsAnHourLonger()
    {
        // 11 pm on Saturday 31 October (daylight time) to 6:45 am on Sunday 1 November (standard time): the hour from 1 to 2 happens twice, so 8 h 45 min pass.
        var now = Edt(10, 31, 23, 0);

        Assert.Equal(new TimeSpan(8, 45, 0), WorkoutWeekSchedule.DelayUntilNext(now, Zone));
    }

    [Fact]
    public void AfterTheChangeNothingShifts()
    {
        // 7 am on the day the clocks went forward: the next 6:45 is tomorrow's, 23 h 45 min on the wall clock and the same on the clock because nothing changes that night.
        var now = Edt(3, 8, 7, 0);

        Assert.Equal(new TimeSpan(23, 45, 0), WorkoutWeekSchedule.DelayUntilNext(now, Zone));
    }

    [Fact]
    public void ASpringDayStillLandsOnTheTime()
    {
        // 6:45 am on the 7th is the last one before the change; the next is the 8th's, 23 h later because the 8th lost an hour.
        var now = Est(3, 7, 6, 45);

        Assert.Equal(TimeSpan.FromHours(23), WorkoutWeekSchedule.DelayUntilNext(now, Zone));
    }

    [Fact]
    public void AnAutumnDayStillLandsOnTheTime()
    {
        var now = Edt(10, 31, 6, 45);

        Assert.Equal(TimeSpan.FromHours(25), WorkoutWeekSchedule.DelayUntilNext(now, Zone));
    }

    [Fact]
    public void AnOrdinaryDayNeverWaitsPastADay()
    {
        for (var minute = 0; minute < 24 * 60; minute += 7)
        {
            var now = Est(11, 4, 0).AddMinutes(minute);
            var delay = WorkoutWeekSchedule.DelayUntilNext(now, Zone);

            Assert.InRange(delay, TimeSpan.FromMinutes(1), TimeSpan.FromHours(24));
        }
    }

    // Foreword's morning is a constant in its Android head; the run is placed after it, so read it, do not restate it.
    private static TimeSpan ForewordsDeliveryTime()
    {
        var path = Path.GetFullPath(Path.Combine(ConstitutionSource.Root(), "..", "Foreword", "Foreword.Android", "DeliveryWorker.cs"));
        Assert.True(File.Exists(path), "Foreword's DeliveryWorker.cs was not found beside Constitution.");
        var text = File.ReadAllText(path);
        var hour = Regex.Match(text, @"DeliveryHour\s*=\s*(\d+)");
        var minute = Regex.Match(text, @"DeliveryMinute\s*=\s*(\d+)");
        Assert.True(hour.Success && minute.Success, "Foreword's delivery time constants were not found.");
        return new TimeSpan(int.Parse(hour.Groups[1].Value), int.Parse(minute.Groups[1].Value), 0);
    }
}
