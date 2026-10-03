using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Microsoft.Extensions.Options;
using Xunit;

namespace Foreword.Tests.Calendar;

public class WorkScheduleAugmenterTests
{
    private static readonly DateOnly Weekday = new(2026, 6, 1);   // Monday
    private static readonly DateOnly Weekend = new(2026, 6, 6);   // Saturday

    private static WorkScheduleAugmenter CreateAugmenter() =>
        new(Options.Create(WorkScheduleOptions.Defaults(Household.Layout)));

    private static CalendarEvent AllDay(PersonColumn column, string title, DateOnly date) =>
        new()
        {
            Title = title,
            Columns = [column],
            IsAllDay = true,
            Start = date.ToDateTime(TimeOnly.MinValue),
            End = date.ToDateTime(TimeOnly.MinValue).AddDays(1)
        };

    [Fact]
    public void Augment_AddsWorkBlocksForBothWorkersOnWeekday()
    {
        var result = CreateAugmenter().Augment([], Weekday);

        var blocks = result.Where(e => e.Kind == EventKind.WorkBlock).ToList();
        Assert.Contains(blocks, b => b.Columns.Contains(Household.Alex));
        Assert.Contains(blocks, b => b.Columns.Contains(Household.Sam));
    }

    [Fact]
    public void Augment_FirstPersonsBlockRunsEightToFour()
    {
        var result = CreateAugmenter().Augment([], Weekday);

        var alex = result.Single(e => e.Kind == EventKind.WorkBlock && e.Columns.Contains(Household.Alex));
        Assert.Equal(new TimeOnly(8, 0), TimeOnly.FromDateTime(alex.Start));
        Assert.Equal(new TimeOnly(16, 0), TimeOnly.FromDateTime(alex.End));
    }

    [Fact]
    public void Augment_SecondPersonsBlockRunsEightToThreeThirty()
    {
        var result = CreateAugmenter().Augment([], Weekday);

        var sam = result.Single(e => e.Kind == EventKind.WorkBlock && e.Columns.Contains(Household.Sam));
        Assert.Equal(new TimeOnly(8, 0), TimeOnly.FromDateTime(sam.Start));
        Assert.Equal(new TimeOnly(15, 30), TimeOnly.FromDateTime(sam.End));
    }

    [Fact]
    public void Augment_NoWorkBlocksOnWeekend()
    {
        var result = CreateAugmenter().Augment([], Weekend);

        Assert.DoesNotContain(result, e => e.Kind == EventKind.WorkBlock);
    }

    [Fact]
    public void Augment_SuppressesWorkBlockWhenAllDayOffEventPresent()
    {
        var off = AllDay(Household.Alex, "Off", Weekday);

        var result = CreateAugmenter().Augment([off], Weekday);

        Assert.DoesNotContain(result, e => e.Kind == EventKind.WorkBlock && e.Columns.Contains(Household.Alex));
        Assert.Contains(result, e => e.Kind == EventKind.WorkBlock && e.Columns.Contains(Household.Sam));
    }

    [Fact]
    public void Augment_MarksAllDayOffEventAsOffDay()
    {
        var off = AllDay(Household.Alex, "Off", Weekday);

        var result = CreateAugmenter().Augment([off], Weekday);

        Assert.Equal(EventKind.OffDay, result.Single(e => e.Title == "Off").Kind);
    }
}
