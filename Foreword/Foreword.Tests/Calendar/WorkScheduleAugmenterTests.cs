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
        new(Options.Create(new WorkScheduleOptions()));

    private static CalendarEvent AllDay(PersonColumn column, string title, DateOnly date) =>
        new()
        {
            Title = title,
            Column = column,
            IsAllDay = true,
            Start = date.ToDateTime(TimeOnly.MinValue),
            End = date.ToDateTime(TimeOnly.MinValue).AddDays(1)
        };

    [Fact]
    public void Augment_AddsWorkBlocksForBothWorkersOnWeekday()
    {
        var result = CreateAugmenter().Augment([], Weekday);

        var blocks = result.Where(e => e.Kind == EventKind.WorkBlock).ToList();
        Assert.Contains(blocks, b => b.Column == PersonColumn.Rowan);
        Assert.Contains(blocks, b => b.Column == PersonColumn.Jordan);
    }

    [Fact]
    public void Augment_MattBlockRunsEightToFour()
    {
        var result = CreateAugmenter().Augment([], Weekday);

        var rowan = result.Single(e => e.Kind == EventKind.WorkBlock && e.Column == PersonColumn.Rowan);
        Assert.Equal(new TimeOnly(8, 0), TimeOnly.FromDateTime(rowan.Start));
        Assert.Equal(new TimeOnly(16, 0), TimeOnly.FromDateTime(rowan.End));
    }

    [Fact]
    public void Augment_AshleyBlockRunsEightToThreeThirty()
    {
        var result = CreateAugmenter().Augment([], Weekday);

        var jordan = result.Single(e => e.Kind == EventKind.WorkBlock && e.Column == PersonColumn.Jordan);
        Assert.Equal(new TimeOnly(8, 0), TimeOnly.FromDateTime(jordan.Start));
        Assert.Equal(new TimeOnly(15, 30), TimeOnly.FromDateTime(jordan.End));
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
        var off = AllDay(PersonColumn.Rowan, "Off", Weekday);

        var result = CreateAugmenter().Augment([off], Weekday);

        Assert.DoesNotContain(result, e => e.Kind == EventKind.WorkBlock && e.Column == PersonColumn.Rowan);
        Assert.Contains(result, e => e.Kind == EventKind.WorkBlock && e.Column == PersonColumn.Jordan);
    }

    [Fact]
    public void Augment_MarksAllDayOffEventAsOffDay()
    {
        var off = AllDay(PersonColumn.Rowan, "Off", Weekday);

        var result = CreateAugmenter().Augment([off], Weekday);

        Assert.Equal(EventKind.OffDay, result.Single(e => e.Title == "Off").Kind);
    }
}
