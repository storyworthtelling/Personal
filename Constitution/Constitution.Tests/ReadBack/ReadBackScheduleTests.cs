using System.Text.RegularExpressions;
using Constitution.Domain.ReadBack;

namespace Constitution.Tests.ReadBack;

/// <summary>D87: the worker runs after Foreword's delivery window, on the phone's own clock.</summary>
public class ReadBackScheduleTests
{
    private static TimeSpan Ours => new(ReadBackSchedule.Hour, ReadBackSchedule.Minute, 0);

    [Fact]
    public void ItRunsAfterForewordHasDelivered()
    {
        var foreword = ForewordsDeliveryTime();

        Assert.True(Ours - foreword >= TimeSpan.FromMinutes(30), $"The read-back at {Ours} is not clear of Foreword's {foreword}.");
    }

    [Fact]
    public void ItRunsInTheMorning()
    {
        Assert.InRange(ReadBackSchedule.Hour, 6, 10);
    }

    [Fact]
    public void BeforeTheHourItWaitsForToday()
    {
        var now = new DateTimeOffset(2026, 11, 4, 5, 0, 0, TimeSpan.FromHours(-5));

        Assert.Equal(Ours - TimeSpan.FromHours(5), ReadBackSchedule.DelayUntilNext(now));
    }

    [Fact]
    public void AfterTheHourItWaitsForTomorrow()
    {
        var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(-5));

        Assert.Equal(TimeSpan.FromHours(12) + Ours, ReadBackSchedule.DelayUntilNext(now));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(9)]
    public void ItReadsTheHourOnTheClockItIsGiven(int offsetHours)
    {
        // A minute before the hour on the owner's own clock is a minute to wait, whatever that clock's offset from UTC.
        var offset = TimeSpan.FromHours(offsetHours);
        var local = new DateTimeOffset(2026, 11, 4, ReadBackSchedule.Hour, ReadBackSchedule.Minute, 0, offset).AddMinutes(-1);

        Assert.Equal(TimeSpan.FromMinutes(1), ReadBackSchedule.DelayUntilNext(local));
    }

    [Fact]
    public void ExactlyOnTheHourWaitsADay()
    {
        var now = new DateTimeOffset(2026, 11, 4, ReadBackSchedule.Hour, ReadBackSchedule.Minute, 0, TimeSpan.FromHours(-5));

        Assert.Equal(TimeSpan.FromHours(24), ReadBackSchedule.DelayUntilNext(now));
    }

    // Foreword's morning is a constant in its Android head; D87 is the promise to stay clear of it, so read it, do not restate it.
    private static TimeSpan ForewordsDeliveryTime()
    {
        var path = Path.GetFullPath(Path.Combine(ConstitutionSource.Root(), "..", "Foreword", "Foreword.Android", "DeliveryWorker.cs"));
        Assert.True(File.Exists(path), "Foreword's DeliveryWorker.cs was not found beside Constitution; D87 cannot be checked.");
        var text = File.ReadAllText(path);
        var hour = Regex.Match(text, @"DeliveryHour\s*=\s*(\d+)");
        var minute = Regex.Match(text, @"DeliveryMinute\s*=\s*(\d+)");
        Assert.True(hour.Success && minute.Success, "Foreword's delivery time constants were not found.");
        return new TimeSpan(int.Parse(hour.Groups[1].Value), int.Parse(minute.Groups[1].Value), 0);
    }
}
