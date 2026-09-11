using Foreword.Domain.Weather;

namespace Foreword.Domain.Tasks;

/// <summary>
/// How many dry hours each day of the week offers, which is what a smart task places itself by.
///
/// <para>Today and the days after it are answered from different data, and the asymmetry is the
/// honest part. Today has an hourly forecast, so the answer is <b>measured</b> — the longest run of
/// hours without rain, which is the same run the weather page draws as its green window. The rest of
/// the week has only a daily chance of rain, so the answer is a <b>judgement</b>: a low enough
/// chance is a working day, anything above it is none. Nothing here invents an hourly forecast for
/// Thursday.</para>
///
/// <para>A day nobody forecast answers zero, the same as a wet one. That is deliberate: a task that
/// needs dry weather and has no forecast has not been shown one, and treating silence as good
/// weather is how the lawn gets scheduled into a storm.</para>
/// </summary>
public static class DryHoursForecast
{
    /// <summary>
    /// What a day counted as dry is worth. A working day rather than twenty-four hours — nobody mows
    /// a lawn at three in the morning, and a number that implied they might would let a task claim
    /// a day it cannot really use.
    /// </summary>
    public const int DryDayHours = 8;

    public static Func<DateOnly, int> From(WeatherReport report)
    {
        var today = report.Today;
        var week = report.Week.ToDictionary(day => day.Date, day => day.PrecipitationPercent);

        return date =>
        {
            if (today is not null && date == today.Date && today.Hours.Count > 0)
            {
                return DryWindow.Find(today.Hours) is { } dry
                    ? dry.End.Hour - dry.Start.Hour + 1
                    : 0;
            }

            return week.TryGetValue(date, out var percent) && percent <= DryWindow.DryPercent
                ? DryDayHours
                : 0;
        };
    }
}
