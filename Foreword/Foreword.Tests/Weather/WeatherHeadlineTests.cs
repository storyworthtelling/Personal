using Foreword.Domain.Weather;
using Xunit;

namespace Foreword.Tests.Weather;

/// <summary>
/// The sentence at the top of the weather page: "Today — high 78° at 3pm, low 57°. Rain clearing by
/// ten."
///
/// <para>It is assembled rather than voiced. The owner's voice has its seats elsewhere on the
/// document, and a forecast is the one thing on the page that must not be embellished — a sentence
/// that says the rain clears when it does not is worse than no sentence, because it is the part
/// that gets read instead of the chart.</para>
/// </summary>
public class WeatherHeadlineTests
{
    private static readonly DateOnly Day = new(2026, 9, 13);

    private static TodayWeather Today(int high, int low, int highHour, params int[] rain) => new()
    {
        Date = Day,
        High = high,
        Low = low,
        HighAt = Day.ToDateTime(new TimeOnly(highHour, 0)),
        Hours = [.. rain.Select((percent, i) =>
            new HourWeather(Day.ToDateTime(new TimeOnly(6 + i, 0)), high, percent))],
    };

    [Fact]
    public void TheTemperaturesAndWhenTheHighLands()
    {
        var line = WeatherHeadline.Describe(Today(78, 57, 15, 0, 0, 0));

        Assert.StartsWith("Today — high 78° at 3pm, low 57°.", line, StringComparison.Ordinal);
    }

    /// <summary>Twelve-hour, like every other time in the suite. Never 15:00.</summary>
    [Fact]
    public void TimesAreTwelveHour()
    {
        Assert.Contains("at 11am", WeatherHeadline.Describe(Today(78, 57, 11, 0, 0, 0)),
            StringComparison.Ordinal);
        Assert.Contains("at 12pm", WeatherHeadline.Describe(Today(78, 57, 12, 0, 0, 0)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RainThatStopsIsSaidToClear()
    {
        // Wet 6-9, dry from 10.
        var line = WeatherHeadline.Describe(Today(78, 57, 15, 80, 70, 55, 40, 5, 0, 0, 0));

        Assert.EndsWith("Rain clearing by 10am.", line, StringComparison.Ordinal);
    }

    [Fact]
    public void RainThatArrivesIsSaidToArrive()
    {
        // Dry 6-10, wet from 11.
        var line = WeatherHeadline.Describe(Today(72, 55, 12, 0, 0, 0, 5, 10, 70, 80, 75));

        Assert.EndsWith("Rain from 11am.", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A dry day says nothing about rain. "No rain expected" is a sentence about an absence, and the
    /// chart underneath already shows a row of empty bars.
    /// </summary>
    [Fact]
    public void ADryDaySaysNothingAboutRain()
    {
        var line = WeatherHeadline.Describe(Today(81, 60, 15, 0, 0, 5, 0, 0));

        Assert.DoesNotContain("Rain", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A day that is wet throughout is not "clearing" and not "from" anything. It gets the plain
    /// statement, because the interesting thing about it is that there is no gap.
    /// </summary>
    [Fact]
    public void ADayOfSolidRainSaysSo()
    {
        var line = WeatherHeadline.Describe(Today(64, 58, 14, 80, 75, 90, 70, 85));

        Assert.EndsWith("Rain all day.", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no hours there is no "at 3pm" to state and nothing to say about rain. The two numbers
    /// still stand — a high and a low come from the daily forecast, which is a different call and
    /// can arrive when the hourly one does not.
    /// </summary>
    [Fact]
    public void WithNoHourlyForecastOnlyTheNumbersAreStated()
    {
        var line = WeatherHeadline.Describe(new TodayWeather { Date = Day, High = 78, Low = 57 });

        Assert.Equal("Today — high 78°, low 57°.", line);
    }
}
