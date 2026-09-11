using Foreword.Domain.Time;

namespace Foreword.Domain.Weather;

/// <summary>
/// The sentence at the top of the weather page: "Today — high 78° at 3pm, low 57°. Rain clearing by
/// 10am."
///
/// <para>Assembled, not voiced. The owner's voice has its seats elsewhere on the document, and a
/// forecast is the one thing here that must not be embellished: a sentence saying the rain clears
/// when it does not is worse than no sentence at all, because it is the part that gets read
/// <i>instead of</i> the chart.</para>
/// </summary>
public static class WeatherHeadline
{
    public static string Describe(TodayWeather today)
    {
        var peak = today.HighAt is { } at && today.Hours.Count > 0
            ? $" at {TimeFormats.Hour12(at)}"
            : string.Empty;

        var line = $"Today — high {today.High}°{peak}, low {today.Low}°.";

        return Rain(today.Hours) is { Length: > 0 } rain ? $"{line} {rain}" : line;
    }

    /// <summary>
    /// What today does about rain, in one clause, or nothing at all. A dry day says nothing: "no
    /// rain expected" is a sentence about an absence, and the row of empty bars underneath already
    /// makes the point without spending a line on it.
    /// </summary>
    private static string Rain(IReadOnlyList<HourWeather> hours)
    {
        if (hours.Count == 0)
        {
            return string.Empty;
        }

        var wet = hours.Where(h => h.PrecipitationPercent > DryWindow.DryPercent).ToList();
        if (wet.Count == 0)
        {
            return string.Empty;
        }

        if (DryWindow.Find(hours) is not { } dry)
        {
            // Wet throughout, with no gap worth naming. The interesting thing about a day like that
            // is precisely that there is nowhere in it to get anything done outdoors.
            return "Rain all day.";
        }

        var first = wet[0].Hour;
        var last = wet[^1].Hour;

        // Which way the day turns: rain that ends before the dry stretch is clearing; rain that
        // starts after it is arriving. A day that does both is described by its ending, because the
        // rain still to come is the half that can still be planned around.
        if (last < dry.Start)
        {
            return $"Rain clearing by {TimeFormats.Hour12(dry.Start)}.";
        }

        if (first > dry.End)
        {
            return $"Rain from {TimeFormats.Hour12(first)}.";
        }

        return dry.End < last
            ? $"Rain from {TimeFormats.Hour12(hours.First(h => h.Hour > dry.End).Hour)}."
            : $"Rain clearing by {TimeFormats.Hour12(dry.Start)}.";
    }
}
