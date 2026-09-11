using Foreword.Domain.Weather;

namespace Foreword.Domain.Interfaces;

/// <summary>
/// Today's weather, the week, and what is in the air — everything the weather page draws.
///
/// <para>One seam over four separate Maps Platform APIs, because the page does not care which call
/// a number came from and every one of them can fail on its own. What comes back is what arrived;
/// a morning with no forecast still has a calendar on it.</para>
/// </summary>
public interface IWeatherClient
{
    Task<WeatherReport> GetReport(DateOnly today);
}

/// <summary>How long it takes to drive between two places, for the calendar's travel bands.</summary>
public interface IRouteClient
{
    /// <summary>
    /// Minutes for each leg asked for, or nothing for a leg that could not be measured. Asked in
    /// one call rather than one per leg, because a day is a handful of drives and a handful of
    /// round trips before six in the morning is a handful of chances to fail.
    /// </summary>
    Task<Weather.TravelLegs> Measure(IReadOnlyList<(string From, string To)> legs, DateTime departAfter);
}
