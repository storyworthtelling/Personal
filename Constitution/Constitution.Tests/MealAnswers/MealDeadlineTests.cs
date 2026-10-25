using Constitution.Domain.Calendar;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.ReadBack;
using Constitution.Tests.Calendar;
using static Constitution.Tests.MealAnswers.MealAnswerData;

namespace Constitution.Tests.MealAnswers;

/// <summary>The deadline as a pure function of the week on the owner's clock, and how it sits against the morning read.</summary>
public class MealDeadlineTests
{
    private static readonly TimeZoneInfo Zone = InventedFamily.Zone;

    [Fact]
    public void TheDeadlineIsSundayMorningOnTheOwnersClock()
    {
        var deadline = MealDeadline.ForWeek(Week, Zone);

        Assert.Equal(DayOfWeek.Sunday, deadline.DayOfWeek);
        Assert.Equal(8, deadline.Hour);
        Assert.Equal(0, deadline.Minute);
        Assert.Equal(Week, DateOnly.FromDateTime(deadline.DateTime));
    }

    [Fact]
    public void OrdinaryWeekIsOnDaylightTime()
    {
        var deadline = MealDeadline.ForWeek(new DateOnly(2026, 10, 25), Zone);
        Assert.Equal(TimeSpan.FromHours(-4), deadline.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public void TheDayTheClocksGoBackIsStillEightOnTheWall()
    {
        // 1 November 2026: the clocks go back at 2 am, so 8 am that Sunday is standard time.
        var deadline = MealDeadline.ForWeek(new DateOnly(2026, 11, 1), Zone);
        Assert.Equal(TimeSpan.FromHours(-5), deadline.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 13, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public void TheDayTheClocksGoForwardIsStillEightOnTheWall()
    {
        // 14 March 2027: forward at 2 am, so 8 am is daylight time.
        var deadline = MealDeadline.ForWeek(new DateOnly(2027, 3, 14), Zone);
        Assert.Equal(TimeSpan.FromHours(-4), deadline.Offset);
        Assert.Equal(new DateTimeOffset(2027, 3, 14, 12, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public void ThursdayToSundayIsNotAlwaysSeventyTwoHours()
    {
        // The sheet goes out Thursday 8 am. Across the fall change the wait is an hour longer; across the spring one, an hour shorter.
        Assert.Equal(TimeSpan.FromHours(72), MealDeadline.ForWeek(new DateOnly(2026, 10, 25), Zone) - ThursdayMorning(new DateOnly(2026, 10, 25)));
        Assert.Equal(TimeSpan.FromHours(73), MealDeadline.ForWeek(new DateOnly(2026, 11, 1), Zone) - ThursdayMorning(new DateOnly(2026, 11, 1)));
        Assert.Equal(TimeSpan.FromHours(71), MealDeadline.ForWeek(new DateOnly(2027, 3, 14), Zone) - ThursdayMorning(new DateOnly(2027, 3, 14)));
    }

    [Fact]
    public void AWeekMustOpenOnASunday()
    {
        Assert.Throws<ArgumentException>(() => MealDeadline.ForWeek(new DateOnly(2026, 10, 26), Zone));
    }

    [Fact]
    public void ADeadlineNeedsAZone()
    {
        Assert.Throws<ArgumentNullException>(() => MealDeadline.ForWeek(Week, null!));
    }

    [Fact]
    public void TheSheetsOwnDeadlineIsTheOne()
    {
        var model = MealSheet.MealSheetSamples.Model();
        Assert.Equal(model.Deadline, MealDeadline.Of(model));
    }

    [Fact]
    public void TheMorningReadComesBeforeTheDeadline()
    {
        // A Saturday denial is read on Sunday's morning read, and that read must count as on time.
        var readsAt = new TimeOnly(ReadBackSchedule.Hour, ReadBackSchedule.Minute);
        Assert.True(readsAt < MealDeadline.TimeOfDay);
    }

    [Theory]
    [InlineData(2026, 10, 25)]
    [InlineData(2026, 11, 1)]
    [InlineData(2027, 3, 14)]
    public void TheMorningReadIsOnTimeInEveryWeek(int year, int month, int day)
    {
        var week = new DateOnly(year, month, day);
        var deadline = MealDeadline.ForWeek(week, Zone);
        var morningRead = ZoneClock.At(week, new TimeOnly(ReadBackSchedule.Hour, ReadBackSchedule.Minute), Zone, ClockEdge.Start);

        Assert.True(morningRead < deadline);
        Assert.True(deadline - morningRead < TimeSpan.FromHours(2));
    }

    [Fact]
    public void SundayMorningsReadSettlesTheWeek()
    {
        // Sam's tablet is read at the morning read: a Saturday no on Tuesday, a tick on Wednesday, silence on Thursday. At the deadline the sheet is settled.
        var deadline = MealDeadline.ForWeek(Week, Zone);
        var read = ZoneClock.At(Week, new TimeOnly(ReadBackSchedule.Hour, ReadBackSchedule.Minute), Zone, ClockEdge.Start);
        var answers = new[]
        {
            Deny(Sam, Tuesday, "Robin has swim", read),
            Tick(Sam, Wednesday, read),
            Silent(Sam, Thursday, read),
            Silent(Lee, Tuesday, read),
            Silent(Lee, Wednesday, read),
            Silent(Lee, Thursday, read),
        };

        var lines = new[] { Tuesday, Wednesday, Thursday };
        var atTheRead = MealDeadline.Decide(answers, deadline, read, lines);
        var atTheDeadline = MealDeadline.Decide(answers, deadline, deadline, lines);

        Assert.All(atTheRead.Items.Where(item => item.ItemId != Tuesday), item => Assert.Equal(MealItemOutcome.Pending, item.Outcome));
        Assert.Equal([Wednesday, Thursday], atTheDeadline.ApprovedItemIds);
        Assert.Equal("Robin has swim", Assert.Single(Assert.Single(atTheDeadline.Denied).Denials).Reason);
        Assert.True(atTheDeadline.IsSettled);
    }

    [Fact]
    public void TheResolverAndTheDecisionAgreeOnSilence()
    {
        // The morning read is before the deadline, so the resolver says Undecided for an untouched line; the decision turns that into approval at 8.
        var resolution = new InkResolution.Undecided(Tuesday);
        var answer = MealAnswerInk.From(resolution, Sam, Week, Before(45))!;
        var item = Only(MealDeadline.Decide([answer], Deadline, Deadline));
        Assert.Equal(MealItemOutcome.Approved, item.Outcome);
    }

    private static DateTimeOffset ThursdayMorning(DateOnly sunday) =>
        ZoneClock.At(sunday.AddDays(-3), new TimeOnly(8, 0), Zone, ClockEdge.Start);
}
