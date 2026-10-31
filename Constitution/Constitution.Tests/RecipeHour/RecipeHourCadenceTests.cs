using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.RecipeHour;

/// <summary>
/// Whether the worker's passes land inside the hour. The hourly pass is at five past; the second hourly one is at thirty-five past, so together they are
/// every thirty minutes. A window of sixty minutes then holds at least two passes, and one that was late or skipped does not lose the page.
/// </summary>
public class RecipeHourCadenceTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    /// <summary>The first pass strictly after <paramref name="instant"/>, from either of the two periodic requests.</summary>
    private static DateTimeOffset NextPass(DateTimeOffset instant) =>
        instant + new[] { MealPassSchedule.DelayUntilNext(instant, Zone), MealPassSchedule.DelayUntilNextRecipeLook(instant, Zone) }.Min();

    /// <summary>Every pass from <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive).</summary>
    private static List<DateTimeOffset> PassesIn(DateTimeOffset from, DateTimeOffset to)
    {
        var passes = new List<DateTimeOffset>();
        for (var next = NextPass(from - TimeSpan.FromTicks(1)); next < to; next = NextPass(next))
        {
            passes.Add(next);
        }

        return passes;
    }

    private static DateTimeOffset At(DateTimeOffset day, int minuteOfDay) => day + TimeSpan.FromMinutes(minuteOfDay);

    // ---- the second request ----

    [Fact]
    public void TheSecondRequestIsAimedAtThirtyFivePast()
    {
        Assert.Equal(35, MealPassSchedule.RecipeMinutePastTheHour);
        Assert.Equal(TimeSpan.FromMinutes(1), MealPassSchedule.DelayUntilNextRecipeLook(Edt(10, 26, 7, 34), Zone));
        Assert.Equal(TimeSpan.FromMinutes(59), MealPassSchedule.DelayUntilNextRecipeLook(Edt(10, 26, 7, 36), Zone));
        Assert.Equal(TimeSpan.FromHours(1), MealPassSchedule.DelayUntilNextRecipeLook(Edt(10, 26, 7, 35), Zone));
        Assert.Equal(TimeSpan.FromMinutes(35), MealPassSchedule.DelayUntilNextRecipeLook(Edt(10, 26, 8), Zone));
    }

    [Fact]
    public void TheSecondRequestReadsTheHouseholdsClockNotUtc()
    {
        // 12:00 UTC is 17:30 on a clock five and a half hours ahead: five minutes to thirty-five past, not thirty-five.
        var ahead = TimeZoneInfo.CreateCustomTimeZone("Invented", TimeSpan.FromMinutes(330), "Invented", "Invented");
        var utc = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromMinutes(5), MealPassSchedule.DelayUntilNextRecipeLook(utc, ahead));
    }

    [Fact]
    public void TheTwoRequestsNeverShareAMinute()
    {
        Assert.NotEqual(MealPassSchedule.MinutePastTheHour, MealPassSchedule.RecipeMinutePastTheHour);
    }

    // ---- every window, every minute ----

    [Fact]
    public void EveryWindowOfFiftyFiveMinutesHoldsAPass()
    {
        // A day with the clock steady, and the two days the clock changes.
        foreach (var day in new[] { Edt(10, 26, 0), Edt(11, 1, 0), Edt(3, 8, 0) })
        {
            for (var start = 0; start < 24 * 60; start++)
            {
                var from = At(day, start);
                var passes = PassesIn(from, from + TimeSpan.FromMinutes(55));
                Assert.True(passes.Count >= 1, $"No pass in the 55 minutes from {from:O}.");
            }
        }
    }

    [Fact]
    public void EveryWindowOfSixtyMinutesHoldsTwoPasses()
    {
        foreach (var day in new[] { Edt(10, 26, 0), Edt(11, 1, 0), Edt(3, 8, 0) })
        {
            for (var start = 0; start < 24 * 60; start++)
            {
                var from = At(day, start);
                var passes = PassesIn(from, from + MealRecipeHour.Hour);
                Assert.True(passes.Count >= 2, $"Fewer than two passes in the hour from {from:O}: {passes.Count}.");
            }
        }
    }

    [Fact]
    public void EveryPassIsAtFiveOrThirtyFivePastOnTheHouseholdsClock()
    {
        var from = Edt(10, 26, 0);
        foreach (var pass in PassesIn(from, from + TimeSpan.FromHours(30)))
        {
            var local = TimeZoneInfo.ConvertTime(pass, Zone);
            Assert.Contains(local.Minute, new[] { 5, 35 });
            Assert.Equal(0, local.Second);
        }
    }

    // ---- the pure check at those passes ----

    private static DinnerChoice CookedAt(DateTimeOffset start) =>
        DinnerChoice.Cooked(DateOnly.FromDateTime(start.DateTime), new RecipeId("lemon-rice"), Sam, start, start.AddMinutes(45), false, false, "From the box.");

    private static MealWeekRecordRead.Held WeekWith(DinnerChoice dinner)
    {
        var weekStart = PlanningWeek.StartOf(dinner.CookStart!.Value, Zone);
        var dinners = Enumerable.Range(0, 7).Select(offset => weekStart.AddDays(offset) == dinner.Day
            ? dinner
            : DinnerChoice.NothingPlanned(weekStart.AddDays(offset), "Nothing is planned."));
        var week = MealWeek.Planned(weekStart, dinners, []);
        return new MealWeekRecordRead.Held(new MealWeekRecord([new StoredMealWeek(week, dinner.CookStart.Value.AddDays(-5), [])]));
    }

    [Fact]
    public void EveryPassInAnHourIsDue()
    {
        var day = Edt(10, 27, 0);
        for (var start = 17 * 60; start < 19 * 60; start++)
        {
            var block = At(day, start);
            var weeks = WeekWith(CookedAt(block));
            var hour = PassesIn(block - MealRecipeHour.Hour, block);

            Assert.True(hour.Count >= 2, $"A block at {block:O} has {hour.Count} passes in its hour.");
            foreach (var pass in hour)
            {
                var week = PlanningWeek.StartOf(pass, Zone);
                var stamp = new MealPassStampRead.Held(new MealPassStamp(pass.AddMinutes(-30), week.AddDays(7), pass.AddMinutes(-30), null));
                var decided = new MealDecisionRecordRead.Held(new MealDecisionRecord([new MealDecisionEntry(week, pass.AddDays(-2), 1, 0, 0, false)]));

                var due = MealPassPlan.Decide(MealPassTrigger.Scheduled, pass, Zone, stamp, weeks, decided, Sam, _ => null);

                Assert.True(due.RecipeHour, $"Not due at {pass:O} for a block at {block:O}.");
            }
        }
    }

    [Fact]
    public void ThePassesJustOutsideTheHourAreNotDue()
    {
        var block = Edt(10, 27, 17, 30);
        var weeks = WeekWith(CookedAt(block));
        foreach (var pass in PassesIn(block - TimeSpan.FromHours(6), block - MealRecipeHour.Hour))
        {
            Assert.False(IsDueAt(weeks, pass), $"Due too early at {pass:O}.");
        }

        foreach (var pass in PassesIn(block, block + TimeSpan.FromHours(3)))
        {
            Assert.False(IsDueAt(weeks, pass), $"Due once the block began, at {pass:O}.");
        }
    }

    private static bool IsDueAt(MealWeekRecordRead weeks, DateTimeOffset pass) => MealRecipeHour.DueWeeks(weeks, pass, Sam, _ => null).Count > 0;
}
