using Foreword.Domain.Tasks;
using Foreword.Domain.Weather;
using Xunit;

namespace Foreword.Tests.Tasks;

/// <summary>
/// How many dry hours a day offers, which is what a smart task places itself by.
///
/// <para>Today and the rest of the week are answered from different data, and that asymmetry is the
/// honest part. Today has an hourly forecast, so the answer is measured: the longest run of hours
/// without rain. The days after it have only a daily chance of rain, so the answer is a judgement —
/// a day whose chance is low enough is treated as dry, and one above it as not. Nothing here invents
/// an hourly forecast for Thursday.</para>
/// </summary>
public class DryHoursForecastTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);

    private static WeatherReport Report(int[] hourlyRain, params (int Day, int Percent)[] week) => new()
    {
        Today = new TodayWeather
        {
            Date = Today,
            Hours =
            [
                .. hourlyRain.Select((percent, i) =>
                    new HourWeather(Today.ToDateTime(new TimeOnly(6 + i, 0)), 70, percent)),
            ],
        },
        Week = [.. week.Select(d => new WeekDay(Today.AddDays(d.Day), "Sunny", 78, 57, d.Percent))],
    };

    [Fact]
    public void TodayIsMeasuredFromItsHours()
    {
        // Wet 6-9, dry 10-13: four dry hours.
        var dryHours = DryHoursForecast.From(Report([80, 70, 55, 40, 0, 0, 0, 0]));

        Assert.Equal(4, dryHours(Today));
    }

    /// <summary>
    /// A day with rain all through it has no dry run at all. Zero rather than "the driest stretch",
    /// because a smart task asks for hours it can work in, not for the least bad part of a wet day.
    /// </summary>
    [Fact]
    public void AWetDayOffersNoDryHours()
    {
        Assert.Equal(0, DryHoursForecast.From(Report([80, 70, 90, 60, 55]))(Today));
    }

    /// <summary>
    /// The rest of the week comes from the daily chance of rain, because that is all there is. A low
    /// chance is a full working day; anything above it is none. Coarse, and deliberately not dressed
    /// up as an hourly figure it is not.
    /// </summary>
    [Fact]
    public void LaterDaysComeFromTheDailyChanceOfRain()
    {
        var dryHours = DryHoursForecast.From(Report([0], (1, 5), (2, 80)));

        Assert.Equal(DryHoursForecast.DryDayHours, dryHours(Today.AddDays(1)));
        Assert.Equal(0, dryHours(Today.AddDays(2)));
    }

    /// <summary>
    /// A day nobody forecast is zero, which is the same answer as a wet one — deliberately. A task
    /// that needs dry weather and has no forecast has not been shown one, and treating silence as
    /// good weather is how the lawn gets scheduled into a storm.
    /// </summary>
    [Fact]
    public void ADayWithNoForecastIsNotAssumedDry()
    {
        var dryHours = DryHoursForecast.From(Report([0], (1, 5)));

        Assert.Equal(0, dryHours(Today.AddDays(5)));
    }

    [Fact]
    public void NoForecastAtAllAnswersZeroForEveryDay()
    {
        var dryHours = DryHoursForecast.From(WeatherReport.Nothing);

        Assert.Equal(0, dryHours(Today));
        Assert.Equal(0, dryHours(Today.AddDays(2)));
    }
}
