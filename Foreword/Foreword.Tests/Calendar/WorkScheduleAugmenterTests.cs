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
        Assert.Contains(blocks, b => b.Column == PersonColumn.Matt);
        Assert.Contains(blocks, b => b.Column == PersonColumn.Ashley);
    }

    [Fact]
    public void Augment_MattBlockRunsEightToFour()
    {
        var result = CreateAugmenter().Augment([], Weekday);

        var matt = result.Single(e => e.Kind == EventKind.WorkBlock && e.Column == PersonColumn.Matt);
        Assert.Equal(new TimeOnly(8, 0), TimeOnly.FromDateTime(matt.Start));
        Assert.Equal(new TimeOnly(16, 0), TimeOnly.FromDateTime(matt.End));
    }

    [Fact]
    public void Augment_AshleyBlockRunsEightToThreeThirty()
    {
        var result = CreateAugmenter().Augment([], Weekday);

        var ashley = result.Single(e => e.Kind == EventKind.WorkBlock && e.Column == PersonColumn.Ashley);
        Assert.Equal(new TimeOnly(8, 0), TimeOnly.FromDateTime(ashley.Start));
        Assert.Equal(new TimeOnly(15, 30), TimeOnly.FromDateTime(ashley.End));
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
        var off = AllDay(PersonColumn.Matt, "Off", Weekday);

        var result = CreateAugmenter().Augment([off], Weekday);

        Assert.DoesNotContain(result, e => e.Kind == EventKind.WorkBlock && e.Column == PersonColumn.Matt);
        Assert.Contains(result, e => e.Kind == EventKind.WorkBlock && e.Column == PersonColumn.Ashley);
    }

    [Fact]
    public void Augment_MarksAllDayOffEventAsOffDay()
    {
        var off = AllDay(PersonColumn.Matt, "Off", Weekday);

        var result = CreateAugmenter().Augment([off], Weekday);

        Assert.Equal(EventKind.OffDay, result.Single(e => e.Title == "Off").Kind);
    }
}
