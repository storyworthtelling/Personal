using Constitution.Domain.Meals.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.MealWorker;

public class MealPassScheduleTests
{
    [Fact]
    public void TheNamesAreTheDecidedNumbers()
    {
        Assert.Equal(TimeSpan.FromHours(1), MealPassSchedule.Interval);
        Assert.Equal(5, MealPassSchedule.MinutePastTheHour);
        Assert.Equal(new TimeOnly(6, 0), MealPassSchedule.PlanFrom);
        Assert.Equal(TimeSpan.FromHours(3), MealPassPlan.RetryGap);
    }

    [Theory]
    [InlineData(6, 0, 0, 5, 0)]
    [InlineData(6, 4, 59, 0, 1)]
    [InlineData(6, 5, 0, 60, 0)]
    [InlineData(6, 30, 0, 35, 0)]
    [InlineData(6, 59, 30, 5, 30)]
    public void TheDelayIsToTheNextFiveMinutesPastTheHour(int hour, int minute, int second, int waitMinutes, int waitSeconds)
    {
        var delay = MealPassSchedule.DelayUntilNext(Edt(10, 26, hour, minute).AddSeconds(second), Zone);

        Assert.Equal(new TimeSpan(0, waitMinutes, waitSeconds), delay);
    }

    [Fact]
    public void TheHouseholdsClockDecidesNotTheInstants()
    {
        var halfHourZone = TimeZoneInfo.CreateCustomTimeZone("Invented Half", TimeSpan.FromMinutes(330), "Invented Half", "Invented Half");
        var utcTen = new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

        // 10:00 UTC reads 15:30 there, so the next :05 is 35 minutes away; on a whole-hour clock it would be 5.
        Assert.Equal(TimeSpan.FromMinutes(35), MealPassSchedule.DelayUntilNext(utcTen, halfHourZone));
    }

    [Fact]
    public void AZoneIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => MealPassSchedule.DelayUntilNext(Edt(10, 26, 6), null!));
    }
}
